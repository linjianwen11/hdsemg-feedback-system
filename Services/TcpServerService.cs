using EMGFeedbackSystem.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EMGFeedbackSystem.Services
{
    public class TcpServerService
    {
        private const int MinPacketLength = 8;
        private const int CrcLength = 2;
        private const ushort CompatSampleLength = 3870;
        private const bool AcceptMissingHeader = true;
        private const bool AcceptInvalidCrc = true;
        private const bool ValidateSamplePacketCrc = false;
        private const int MaxCapturedSamplePackets = 3000;
        private const int CapturePrefixBytes = 96;
        private const ushort FrameHeaderV1 = 0x1B06;
        private const ushort FrameHeaderV2 = 0x1B07;
        private const ushort OutgoingHeader = FrameHeaderV1;

        private TcpListener? _listener;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cts;
        private readonly object _lockObj = new object();
        private readonly object _captureLock = new object();
        private int _serverState; // 0=stopped, 1=starting/listening
        private bool _loggedFirstSamplePacket;
        private string? _captureFilePath;
        private int _capturedSampleCount;
        private readonly List<string> _capturedRows = new List<string>(MaxCapturedSamplePackets + 1);

        public event EventHandler<bool>? ConnectionStatusChanged;
        public event EventHandler<EMGData>? DataReceived;
        public event EventHandler<string>? LogMessage;

        public bool IsConnected => _client?.Connected ?? false;
        public string ServerIp { get; set; } = "0.0.0.0";
        public int ServerPort { get; set; } = 1234;

        public async Task StartServerAsync()
        {
            if (Interlocked.CompareExchange(ref _serverState, 1, 0) != 0)
            {
                throw new InvalidOperationException("Server is already starting or listening.");
            }

            try
            {
                _cts = new CancellationTokenSource();
                _loggedFirstSamplePacket = false;

                var ipAddress = IPAddress.Parse(ServerIp);
                _listener = new TcpListener(ipAddress, ServerPort);
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Start();
                InitializeCaptureSession();

                EmitLog($"Server started, waiting for {ServerIp}:{ServerPort}");

                _client = await _listener.AcceptTcpClientAsync();
                _stream = _client.GetStream();

                EmitLog("Client connected.");
                ConnectionStatusChanged?.Invoke(this, true);

                _ = Task.Run(() => ReceiveDataAsync(_cts.Token), _cts.Token);
            }
            catch (ObjectDisposedException) when (Volatile.Read(ref _serverState) == 0)
            {
                EmitLog("Server start canceled.");
            }
            catch (SocketException ex) when (
                Volatile.Read(ref _serverState) == 0 ||
                ex.SocketErrorCode == SocketError.OperationAborted ||
                ex.SocketErrorCode == SocketError.Interrupted)
            {
                EmitLog("Server start canceled.");
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _serverState, 0);
                try
                {
                    _cts?.Cancel();
                    _stream?.Close();
                    _client?.Close();
                    _listener?.Stop();
                }
                catch
                {
                }
                finally
                {
                    _cts?.Dispose();
                    _cts = null;
                    _stream = null;
                    _client = null;
                    _listener = null;
                }
                EmitLog($"Start server failed: {ex.Message}");
                throw;
            }
        }

        public void StopServer()
        {
            try
            {
                Interlocked.Exchange(ref _serverState, 0);
                _cts?.Cancel();
                _stream?.Close();
                _client?.Close();
                _listener?.Stop();
                _cts?.Dispose();
                _cts = null;
                _stream = null;
                _client = null;
                _listener = null;

                EmitLog("Server stopped.");
                if (!string.IsNullOrWhiteSpace(_captureFilePath))
                {
                    FlushCaptureToDisk();
                    EmitLog($"Packet capture saved: {_captureFilePath}");
                }
                ConnectionStatusChanged?.Invoke(this, false);
            }
            catch (Exception ex)
            {
                EmitLog($"Stop server error: {ex.Message}");
            }
        }

        private async Task ReceiveDataAsync(CancellationToken ct)
        {
            var buffer = new byte[4096];
            var packetBuffer = new List<byte>();
            bool loggedFirstRead = false;

            try
            {
                while (!ct.IsCancellationRequested && _stream != null)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, ct);

                    if (bytesRead == 0)
                    {
                        EmitLog("Client disconnected.");
                        ConnectionStatusChanged?.Invoke(this, false);
                        break;
                    }

                    if (!loggedFirstRead)
                    {
                        loggedFirstRead = true;
                        int previewLen = Math.Min(bytesRead, 16);
                        string preview = BitConverter.ToString(buffer, 0, previewLen);
                        EmitLog($"[FirstRead] bytesRead={bytesRead}, preview={preview}");
                    }

                    packetBuffer.AddRange(buffer.Take(bytesRead));
                    ProcessPackets(packetBuffer);
                }
            }
            catch (OperationCanceledException)
            {
                EmitLog("Receive canceled.");
            }
            catch (Exception ex)
            {
                EmitLog($"Receive error: {ex.Message}");
                ConnectionStatusChanged?.Invoke(this, false);
            }
        }

        private void ProcessPackets(List<byte> buffer)
        {
            while (buffer.Count >= MinPacketLength)
            {
                int frameStart = FindFrameHeader(buffer);
                bool hasHeader = frameStart == 0;

                if (frameStart > 0)
                {
                    buffer.RemoveRange(0, frameStart);
                    hasHeader = true;
                }

                if (!hasHeader && !AcceptMissingHeader)
                {
                    break;
                }

                if (buffer.Count < MinPacketLength)
                {
                    break;
                }

                ushort length = (ushort)((buffer[4] << 8) | buffer[5]);
                if (length < MinPacketLength)
                {
                    buffer.RemoveAt(0);
                    continue;
                }

                if (buffer.Count < length)
                {
                    break;
                }

                byte[] packet = buffer.Take(length).ToArray();
                buffer.RemoveRange(0, length);

                bool isSamplePacket = IsSamplePacket(packet);
                bool crcValid = (!isSamplePacket || ValidateSamplePacketCrc) ? IsCrcValid(packet) : true;

                if (!crcValid && !AcceptInvalidCrc)
                {
                    EmitLog("CRC failed, packet dropped.");
                    continue;
                }

                if (!crcValid && AcceptInvalidCrc)
                {
                    EmitLog("CRC failed, continue in compat mode.");
                }

                try
                {
                    ParsePacket(packet);
                }
                catch (Exception ex)
                {
                    EmitLog($"Parse packet failed: {ex.Message}");
                }
            }
        }

        private static bool IsSamplePacket(byte[] packet)
        {
            return packet.Length >= MinPacketLength
                && packet[6] == Protocol.Category.SampleData
                && packet[7] == Protocol.Function.UploadSample;
        }

        private bool IsCrcValid(byte[] packet)
        {
            if (packet.Length < MinPacketLength + CrcLength)
            {
                return true;
            }

            ushort expectedHighFirst = (ushort)((packet[^2] << 8) | packet[^1]);
            ushort expectedLowFirst = (ushort)((packet[^1] << 8) | packet[^2]);
            ushort actualModbus = CalculateCrc16Modbus(packet, packet.Length - CrcLength);
            ushort actualCcitt = CalculateCrc16CcittFalse(packet, packet.Length - CrcLength);

            bool modbusMatched = expectedHighFirst == actualModbus || expectedLowFirst == actualModbus;
            bool ccittMatched = expectedHighFirst == actualCcitt || expectedLowFirst == actualCcitt;
            return modbusMatched || ccittMatched;
        }

        private int FindFrameHeader(List<byte> buffer)
        {
            for (int i = 0; i < buffer.Count - 1; i++)
            {
                ushort header = (ushort)((buffer[i] << 8) | buffer[i + 1]);
                if (header == FrameHeaderV1 || header == FrameHeaderV2 || header == Protocol.FrameHeader)
                {
                    return i;
                }
            }
            return -1;
        }

        private void ParsePacket(byte[] packet)
        {
            if (packet.Length < MinPacketLength)
            {
                return;
            }

            byte categoryId = packet[6];
            byte functionId = packet[7];

            if (categoryId == Protocol.Category.SampleData && functionId == Protocol.Function.UploadSample)
            {
                CaptureSamplePacket(packet);
                if (packet.Length < Protocol.Length.SampleData)
                {
                    EmitLog($"Invalid sample packet length: {packet.Length}, expected at least: {Protocol.Length.SampleData}");
                    return;
                }

                if (packet.Length != Protocol.Length.SampleData && packet.Length != CompatSampleLength)
                {
                    EmitLog($"Non-standard sample packet length: {packet.Length}, expected: {Protocol.Length.SampleData}.");
                }

                LogFirstSamplePacket(packet);
                ParseSampleData(packet);
                return;
            }

            if (categoryId == Protocol.Category.BasicFunction && functionId == Protocol.Function.HandshakeAck)
            {
                EmitLog("Handshake ack received.");
                return;
            }

            if (categoryId == Protocol.Category.ControlCommand)
            {
                if (functionId == Protocol.Function.StartAck)
                {
                    byte result = packet.Length > 8 ? packet[8] : (byte)0;
                    EmitLog($"Start ack: {(result == Protocol.Result.Success ? "Success" : "Failure")}");
                }
                else if (functionId == Protocol.Function.StopAck)
                {
                    byte result = packet.Length > 8 ? packet[8] : (byte)0;
                    EmitLog($"Stop ack: {(result == Protocol.Result.Success ? "Success" : "Failure")}");
                }
            }
        }

        private void ParseSampleData(byte[] packet)
        {
            var emgData = new EMGData
            {
                SequenceNumber = (ushort)((packet[8] << 8) | packet[9]),
                BatteryVoltage = (ushort)((packet[10] << 8) | packet[11])
            };

            if (packet.Length == Protocol.Length.SampleData)
            {
                ParseStandardSampleData(packet, emgData);
            }
            else
            {
                ParseNonStandardSampleData(packet, emgData);
            }

            DataReceived?.Invoke(this, emgData);
        }

        private static void ParseStandardSampleData(byte[] packet, EMGData emgData)
        {
            int dataOffset = 12;
            int channelCount = Protocol.Channels.Total;
            int bytesPerValue = Protocol.ChannelDataBytes;
            int bytesPerChannel = bytesPerValue * 2;

            for (int i = 0; i < channelCount; i++)
            {
                int rawOffset = dataOffset + i * bytesPerChannel;
                int absOffset = rawOffset + bytesPerValue;
                if (rawOffset + bytesPerValue <= packet.Length - CrcLength &&
                    absOffset + bytesPerValue <= packet.Length - CrcLength)
                {
                    emgData.ChannelValues[i] = ReadBigEndianFloat(packet, rawOffset);
                    emgData.AbsMeanValues[i] = ReadBigEndianFloat(packet, absOffset);
                }
            }
        }

        private void ParseNonStandardSampleData(byte[] packet, EMGData emgData)
        {
            int dataOffset = 12;
            int payloadLength = packet.Length - dataOffset - CrcLength;
            int channelCount = Protocol.Channels.Total;
            if (payloadLength <= 0 || payloadLength < channelCount)
            {
                return;
            }

            if (TryParseInt16FrameMajor(packet, dataOffset, payloadLength, channelCount, emgData, out int prefixBytes, out int frameCount, out string int16Mode))
            {
                NormalizeCompatEnergies(emgData);
                EmitLog($"[CompatParse] mode={int16Mode}, packetLen={packet.Length}, payload={payloadLength}, prefix={prefixBytes}, frames={frameCount}, channels={channelCount}");
                return;
            }

            if (TryParseFloatFrameMajor(packet, dataOffset, payloadLength, channelCount, emgData, out prefixBytes, out frameCount, out string floatMode))
            {
                NormalizeCompatEnergies(emgData);
                EmitLog($"[CompatParse] mode={floatMode}, packetLen={packet.Length}, payload={payloadLength}, prefix={prefixBytes}, frames={frameCount}, channels={channelCount}");
                return;
            }

            if (TryParseFrameMajor(packet, dataOffset, payloadLength, channelCount, emgData, out prefixBytes, out frameCount))
            {
                NormalizeCompatEnergies(emgData);
                EmitLog($"[CompatParse] mode=frame-major, packetLen={packet.Length}, payload={payloadLength}, prefix={prefixBytes}, frames={frameCount}, channels={channelCount}");
                return;
            }

            int bytesPerChannel = payloadLength / channelCount;
            if (bytesPerChannel <= 0)
            {
                return;
            }

            for (int i = 0; i < channelCount; i++)
            {
                int start = dataOffset + i * bytesPerChannel;
                int endExclusive = (i == channelCount - 1) ? dataOffset + payloadLength : start + bytesPerChannel;

                if (start < 0 || endExclusive > packet.Length - CrcLength || endExclusive <= start)
                {
                    continue;
                }

                double energy = EstimateBlockEnergy(packet, start, endExclusive);
                emgData.ChannelValues[i] = energy;
                emgData.AbsMeanValues[i] = energy;
            }

            NormalizeCompatEnergies(emgData);
            EmitLog($"[CompatParse] mode=channel-major-fallback, packetLen={packet.Length}, payload={payloadLength}, bytesPerCh≈{bytesPerChannel}");
        }

        private static double EstimateBlockEnergy(byte[] packet, int start, int endExclusive)
        {
            double sum = 0;
            int count = 0;
            for (int i = start; i < endExclusive; i++)
            {
                int centered = packet[i] - 128;
                sum += Math.Abs(centered);
                count++;
            }

            return count == 0 ? 0 : (sum / count);
        }

        private static void NormalizeCompatEnergies(EMGData emgData)
        {
            int len = Math.Min(emgData.AbsMeanValues.Length, Protocol.Channels.Total);
            if (len <= 0)
            {
                return;
            }

            double maxSignal = 0;
            for (int i = 0; i < len; i++)
            {
                double v = emgData.AbsMeanValues[i];
                if (double.IsNaN(v) || double.IsInfinity(v))
                {
                    v = 0;
                }

                if (v < 0)
                {
                    v = -v;
                }

                emgData.AbsMeanValues[i] = v;
                emgData.ChannelValues[i] = v;
                if (v > maxSignal)
                {
                    maxSignal = v;
                }
            }

            if (maxSignal <= 0)
            {
                return;
            }

            for (int i = 0; i < len; i++)
            {
                double normalized = emgData.AbsMeanValues[i] / maxSignal;
                emgData.AbsMeanValues[i] = normalized;
                emgData.ChannelValues[i] = normalized;
            }
        }

        private static bool TryParseFloatFrameMajor(
            byte[] packet,
            int dataOffset,
            int payloadLength,
            int channelCount,
            EMGData emgData,
            out int prefixBytes,
            out int frameCount,
            out string mode)
        {
            prefixBytes = 0;
            frameCount = 0;
            mode = string.Empty;

            double bestScore = double.NegativeInfinity;
            int bestPrefix = 0;
            int bestFrames = 0;
            bool bestBigEndian = true;
            double[]? bestEnergy = null;

            int[] prefixCandidates = { 0, 16, 32, 64 };
            foreach (int prefix in prefixCandidates)
            {
                int usable = payloadLength - prefix;
                if (usable <= 0 || usable % (channelCount * 4) != 0)
                {
                    continue;
                }

                int frames = usable / (channelCount * 4);
                if (frames < 2)
                {
                    continue;
                }

                int baseOffset = dataOffset + prefix;
                int endOffset = baseOffset + usable;
                if (endOffset > packet.Length - CrcLength)
                {
                    continue;
                }

                foreach (bool bigEndian in new[] { true, false })
                {
                    var energy = new double[channelCount];
                    bool valid = true;
                    for (int ch = 0; ch < channelCount; ch++)
                    {
                        double sum = 0;
                        for (int f = 0; f < frames; f++)
                        {
                            int idx = baseOffset + (f * channelCount + ch) * 4;
                            float sample = bigEndian ? ReadBigEndianFloat(packet, idx) : BitConverter.ToSingle(packet, idx);
                            if (float.IsNaN(sample) || float.IsInfinity(sample))
                            {
                                valid = false;
                                break;
                            }

                            sum += Math.Abs(sample);
                        }

                        if (!valid)
                        {
                            break;
                        }

                        energy[ch] = sum / frames;
                    }

                    if (!valid)
                    {
                        continue;
                    }

                    double mean = energy.Average();
                    double variance = 0;
                    for (int i = 0; i < energy.Length; i++)
                    {
                        double d = energy[i] - mean;
                        variance += d * d;
                    }
                    variance /= energy.Length;

                    if (variance > bestScore)
                    {
                        bestScore = variance;
                        bestPrefix = prefix;
                        bestFrames = frames;
                        bestBigEndian = bigEndian;
                        bestEnergy = energy;
                    }
                }
            }

            if (bestEnergy == null)
            {
                return false;
            }

            for (int i = 0; i < channelCount; i++)
            {
                emgData.ChannelValues[i] = bestEnergy[i];
                emgData.AbsMeanValues[i] = bestEnergy[i];
            }

            prefixBytes = bestPrefix;
            frameCount = bestFrames;
            mode = bestBigEndian ? "float-frame-major-be" : "float-frame-major-le";
            return true;
        }

        private static bool TryParseInt16FrameMajor(
            byte[] packet,
            int dataOffset,
            int payloadLength,
            int channelCount,
            EMGData emgData,
            out int prefixBytes,
            out int frameCount,
            out string mode)
        {
            prefixBytes = 0;
            frameCount = 0;
            mode = string.Empty;

            double bestScore = double.NegativeInfinity;
            int bestPrefix = 0;
            int bestFrames = 0;
            bool bestBigEndian = true;
            double[]? bestEnergy = null;

            int[] prefixCandidates = { 0, 16, 32, 64 };
            foreach (int prefix in prefixCandidates)
            {
                int usable = payloadLength - prefix;
                if (usable <= 0 || usable % (channelCount * 2) != 0)
                {
                    continue;
                }

                int frames = usable / (channelCount * 2);
                if (frames < 4)
                {
                    continue;
                }

                int baseOffset = dataOffset + prefix;
                int endOffset = baseOffset + usable;
                if (endOffset > packet.Length - CrcLength)
                {
                    continue;
                }

                foreach (bool bigEndian in new[] { true, false })
                {
                    var energy = new double[channelCount];
                    for (int ch = 0; ch < channelCount; ch++)
                    {
                        double sum = 0;
                        for (int f = 0; f < frames; f++)
                        {
                            int idx = baseOffset + (f * channelCount + ch) * 2;
                            short sample = bigEndian
                                ? (short)((packet[idx] << 8) | packet[idx + 1])
                                : (short)(packet[idx] | (packet[idx + 1] << 8));

                            int sampleInt = sample;
                            if (sampleInt == short.MinValue)
                            {
                                sampleInt = short.MaxValue;
                            }
                            sum += Math.Abs(sampleInt);
                        }

                        energy[ch] = sum / frames;
                    }

                    double mean = energy.Average();
                    double variance = 0;
                    for (int i = 0; i < energy.Length; i++)
                    {
                        double d = energy[i] - mean;
                        variance += d * d;
                    }
                    variance /= energy.Length;

                    if (variance > bestScore)
                    {
                        bestScore = variance;
                        bestPrefix = prefix;
                        bestFrames = frames;
                        bestBigEndian = bigEndian;
                        bestEnergy = energy;
                    }
                }
            }

            if (bestEnergy == null)
            {
                return false;
            }

            for (int i = 0; i < channelCount; i++)
            {
                emgData.ChannelValues[i] = bestEnergy[i];
                emgData.AbsMeanValues[i] = bestEnergy[i];
            }

            prefixBytes = bestPrefix;
            frameCount = bestFrames;
            mode = bestBigEndian ? "int16-frame-major-be" : "int16-frame-major-le";
            return true;
        }

        private static bool TryParseFrameMajor(
            byte[] packet,
            int dataOffset,
            int payloadLength,
            int channelCount,
            EMGData emgData,
            out int prefixBytes,
            out int frameCount)
        {
            prefixBytes = 0;
            frameCount = 0;

            int[] prefixCandidates = { 0, 16, 32, 64 };
            foreach (int prefix in prefixCandidates)
            {
                int usable = payloadLength - prefix;
                if (usable <= 0 || usable % channelCount != 0)
                {
                    continue;
                }

                int frames = usable / channelCount;
                if (frames < 4)
                {
                    continue;
                }

                int baseOffset = dataOffset + prefix;
                int endOffset = baseOffset + usable;
                if (endOffset > packet.Length - CrcLength)
                {
                    continue;
                }

                for (int ch = 0; ch < channelCount; ch++)
                {
                    double sum = 0;
                    for (int f = 0; f < frames; f++)
                    {
                        int idx = baseOffset + f * channelCount + ch;
                        int centered = packet[idx] - 128;
                        sum += Math.Abs(centered);
                    }

                    double energy = sum / frames;
                    emgData.ChannelValues[ch] = energy;
                    emgData.AbsMeanValues[ch] = energy;
                }

                prefixBytes = prefix;
                frameCount = frames;
                return true;
            }

            return false;
        }

        private static float ReadBigEndianFloat(byte[] packet, int offset)
        {
            byte[] be = new[] { packet[offset + 3], packet[offset + 2], packet[offset + 1], packet[offset] };
            return BitConverter.ToSingle(be, 0);
        }

        private void LogFirstSamplePacket(byte[] packet)
        {
            if (_loggedFirstSamplePacket)
            {
                return;
            }

            _loggedFirstSamplePacket = true;

            int lengthField = packet.Length >= 6 ? ((packet[4] << 8) | packet[5]) : -1;
            string headerBytes = packet.Length >= 8
                ? BitConverter.ToString(packet, 0, 8)
                : BitConverter.ToString(packet);

            int dataOffset = 12;
            string sampleBytes = packet.Length >= dataOffset + 8
                ? BitConverter.ToString(packet, dataOffset, 8)
                : "N/A";

            string absMeanBytes = packet.Length >= dataOffset + 4 + (Protocol.Channels.Total * 4)
                ? BitConverter.ToString(packet, dataOffset + (Protocol.Channels.Total * 4), 8)
                : "N/A";

            float leSample = 0f;
            float beSample = 0f;
            if (packet.Length >= dataOffset + 4)
            {
                leSample = BitConverter.ToSingle(packet, dataOffset);
                byte[] be = new[] { packet[dataOffset + 3], packet[dataOffset + 2], packet[dataOffset + 1], packet[dataOffset] };
                beSample = BitConverter.ToSingle(be, 0);
            }

            EmitLog($"[FirstSample] lenField={lengthField}, packetLen={packet.Length}, header={headerBytes}");
            EmitLog($"[FirstSample] sampleBytes={sampleBytes}, leFloat={leSample}, beFloat={beSample}");
            EmitLog($"[FirstSample] absMeanBytes={absMeanBytes}");
        }

        public async Task SendCommandAsync(byte categoryId, byte functionId, byte[]? data = null)
        {
            if (_stream == null || _client?.Connected != true)
            {
                throw new InvalidOperationException("Not connected to client.");
            }

            var packet = BuildPacket(categoryId, functionId, data);
            EmitLog($"[Send] src=0x{packet[2]:X2}, dst=0x{packet[3]:X2}, cat=0x{categoryId:X2}, func=0x{functionId:X2}, len={packet.Length}, bytes={BitConverter.ToString(packet)}");

            lock (_lockObj)
            {
                _stream.Write(packet, 0, packet.Length);
            }

            await Task.CompletedTask;
        }

        private byte[] BuildPacket(byte categoryId, byte functionId, byte[]? data)
        {
            int dataLength = data?.Length ?? 0;
            int packetLength = 8 + dataLength + 2;

            var packet = new byte[packetLength];
            packet[0] = (byte)(OutgoingHeader >> 8);
            packet[1] = (byte)(OutgoingHeader & 0xFF);
            packet[2] = Protocol.SourceUpper;
            packet[3] = Protocol.DestLower;
            packet[4] = (byte)(packetLength >> 8);
            packet[5] = (byte)(packetLength & 0xFF);
            packet[6] = categoryId;
            packet[7] = functionId;

            if (data != null && data.Length > 0)
            {
                Buffer.BlockCopy(data, 0, packet, 8, data.Length);
            }

            ushort crc = CalculateCrc16Modbus(packet, packetLength - CrcLength);
            // Device expects Modbus CRC in low-byte-first order.
            packet[packetLength - 2] = (byte)(crc & 0xFF);
            packet[packetLength - 1] = (byte)(crc >> 8);
            return packet;
        }

        private static ushort CalculateCrc16Modbus(byte[] data, int length)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        crc >>= 1;
                        crc ^= 0xA001;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }
            return crc;
        }

        private static ushort CalculateCrc16CcittFalse(byte[] data, int length)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < length; i++)
            {
                crc ^= (ushort)(data[i] << 8);
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x8000) != 0)
                    {
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    }
                    else
                    {
                        crc <<= 1;
                    }
                }
            }
            return crc;
        }

        public async Task SendHandshakeAsync()
        {
            await SendCommandAsync(Protocol.Category.BasicFunction, Protocol.Function.Handshake);
            EmitLog("Sent handshake command.");
        }

        public async Task SendStartCollectionAsync()
        {
            await SendCommandAsync(Protocol.Category.ControlCommand, Protocol.Function.StartCollection);
            EmitLog("Sent start collection command.");
        }

        public async Task SendStopCollectionAsync()
        {
            await SendCommandAsync(Protocol.Category.ControlCommand, Protocol.Function.StopCollection);
            EmitLog("Sent stop collection command.");
        }

        private void InitializeCaptureSession()
        {
            lock (_captureLock)
            {
                string captureDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "EMGFeedbackSystem",
                    "packet-captures");
                Directory.CreateDirectory(captureDir);

                string fileName = $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
                _captureFilePath = Path.Combine(captureDir, fileName);
                _capturedSampleCount = 0;
                _capturedRows.Clear();
                _capturedRows.Add("Timestamp,PacketLength,LengthField,Sequence,Battery,PayloadLength,PayloadSha256,PacketPrefixHex");
            }
        }

        private void CaptureSamplePacket(byte[] packet)
        {
            lock (_captureLock)
            {
                if (string.IsNullOrWhiteSpace(_captureFilePath) || _capturedSampleCount >= MaxCapturedSamplePackets)
                {
                    return;
                }

                int lengthField = packet.Length >= 6 ? ((packet[4] << 8) | packet[5]) : 0;
                int sequence = packet.Length >= 10 ? ((packet[8] << 8) | packet[9]) : 0;
                int battery = packet.Length >= 12 ? ((packet[10] << 8) | packet[11]) : 0;

                int payloadOffset = 12;
                int payloadLength = Math.Max(0, packet.Length - payloadOffset - CrcLength);
                byte[] payload = payloadLength > 0
                    ? packet.Skip(payloadOffset).Take(payloadLength).ToArray()
                    : Array.Empty<byte>();
                string payloadHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

                int prefixLen = Math.Min(CapturePrefixBytes, packet.Length);
                string prefixHex = BitConverter.ToString(packet, 0, prefixLen).Replace("-", string.Empty).ToLowerInvariant();

                string line = string.Join(",",
                    DateTime.Now.ToString("O"),
                    packet.Length,
                    lengthField,
                    sequence,
                    battery,
                    payloadLength,
                    payloadHash,
                    prefixHex);

                _capturedRows.Add(line);
                _capturedSampleCount++;
            }
        }

        private void FlushCaptureToDisk()
        {
            lock (_captureLock)
            {
                if (string.IsNullOrWhiteSpace(_captureFilePath) || _capturedRows.Count == 0)
                {
                    return;
                }

                File.WriteAllText(_captureFilePath, string.Join(Environment.NewLine, _capturedRows), Encoding.UTF8);
            }
        }

        private void EmitLog(string message)
        {
            LogMessage?.Invoke(this, message);
            Debug.WriteLine(message);
        }
    }
}
