using EMGFeedbackSystem.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace EMGFeedbackSystem.Services
{
    public class TcpServerService
    {
        private const int MinPacketLength = 8;
        private const int CrcLength = 2;
        private const bool AcceptMissingHeader = true;
        private const bool AcceptInvalidCrc = true;

        private TcpListener? _listener;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cts;
        private readonly object _lockObj = new object();

        public event EventHandler<bool>? ConnectionStatusChanged;
        public event EventHandler<EMGData>? DataReceived;
        public event EventHandler<string>? LogMessage;

        public bool IsConnected => _client?.Connected ?? false;
        public string ServerIp { get; set; } = "192.168.4.2";
        public int ServerPort { get; set; } = 1234;

        public async Task StartServerAsync()
        {
            try
            {
                _cts = new CancellationTokenSource();

                var ipAddress = IPAddress.Parse(ServerIp);
                _listener = new TcpListener(ipAddress, ServerPort);
                _listener.Start();

                LogMessage?.Invoke(this, $"Server started, waiting for {ServerIp}:{ServerPort}");

                _client = await _listener.AcceptTcpClientAsync();
                _stream = _client.GetStream();

                LogMessage?.Invoke(this, "Client connected.");
                ConnectionStatusChanged?.Invoke(this, true);

                _ = Task.Run(() => ReceiveDataAsync(_cts.Token), _cts.Token);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Start server failed: {ex.Message}");
                throw;
            }
        }

        public void StopServer()
        {
            try
            {
                _cts?.Cancel();
                _stream?.Close();
                _client?.Close();
                _listener?.Stop();

                LogMessage?.Invoke(this, "Server stopped.");
                ConnectionStatusChanged?.Invoke(this, false);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Stop server error: {ex.Message}");
            }
        }

        private async Task ReceiveDataAsync(CancellationToken ct)
        {
            var buffer = new byte[4096];
            var packetBuffer = new List<byte>();

            try
            {
                while (!ct.IsCancellationRequested && _stream != null)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, ct);

                    if (bytesRead == 0)
                    {
                        LogMessage?.Invoke(this, "Client disconnected.");
                        ConnectionStatusChanged?.Invoke(this, false);
                        break;
                    }

                    packetBuffer.AddRange(buffer.Take(bytesRead));
                    ProcessPackets(packetBuffer);
                }
            }
            catch (OperationCanceledException)
            {
                LogMessage?.Invoke(this, "Receive canceled.");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Receive error: {ex.Message}");
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

                if (!IsCrcValid(packet) && !AcceptInvalidCrc)
                {
                    LogMessage?.Invoke(this, "CRC failed, packet dropped.");
                    continue;
                }

                if (!IsCrcValid(packet) && AcceptInvalidCrc)
                {
                    LogMessage?.Invoke(this, "CRC failed, continue in compat mode.");
                }

                try
                {
                    ParsePacket(packet);
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke(this, $"Parse packet failed: {ex.Message}");
                }
            }
        }

        private bool IsCrcValid(byte[] packet)
        {
            if (packet.Length <= MinPacketLength + CrcLength)
            {
                return true;
            }

            ushort expected = (ushort)((packet[^2] << 8) | packet[^1]);
            ushort actual = CalculateCrc16(packet, packet.Length - CrcLength);
            return expected == actual;
        }

        private int FindFrameHeader(List<byte> buffer)
        {
            for (int i = 0; i < buffer.Count - 1; i++)
            {
                ushort header = (ushort)((buffer[i] << 8) | buffer[i + 1]);
                if (header == Protocol.FrameHeader)
                {
                    return i;
                }
            }
            return -1;
        }

        private void ParsePacket(byte[] packet)
        {
            if (packet.Length < MinPacketLength) return;

            byte categoryId = packet[6];
            byte functionId = packet[7];

            if (categoryId == Protocol.Category.SampleData && functionId == Protocol.Function.UploadSample)
            {
                ParseSampleData(packet);
            }
            else if (categoryId == Protocol.Category.BasicFunction)
            {
                if (functionId == Protocol.Function.HandshakeAck)
                {
                    LogMessage?.Invoke(this, "Handshake ack received.");
                }
            }
            else if (categoryId == Protocol.Category.ControlCommand)
            {
                if (functionId == Protocol.Function.StartAck)
                {
                    byte result = packet.Length > 8 ? packet[8] : (byte)0;
                    LogMessage?.Invoke(this, $"Start ack: {(result == Protocol.Result.Success ? "Success" : "Failure")}");
                }
                else if (functionId == Protocol.Function.StopAck)
                {
                    byte result = packet.Length > 8 ? packet[8] : (byte)0;
                    LogMessage?.Invoke(this, $"Stop ack: {(result == Protocol.Result.Success ? "Success" : "Failure")}");
                }
            }
        }

        private void ParseSampleData(byte[] packet)
        {
            if (packet.Length < Protocol.Length.SampleData) return;

            var emgData = new EMGData
            {
                SequenceNumber = (ushort)((packet[8] << 8) | packet[9]),
                BatteryVoltage = (ushort)((packet[10] << 8) | packet[11])
            };

            int dataOffset = 12;
            int channelCount = Protocol.Channels.Total;

            for (int i = 0; i < channelCount; i++)
            {
                int offset = dataOffset + i * 4;
                if (offset + 4 <= packet.Length - 2)
                {
                    byte[] floatBytes = new[] { packet[offset + 3], packet[offset + 2], packet[offset + 1], packet[offset] };
                    emgData.ChannelValues[i] = BitConverter.ToSingle(floatBytes, 0);
                }
            }

            int absMeanOffset = dataOffset + channelCount * 4;
            for (int i = 0; i < channelCount; i++)
            {
                int offset = absMeanOffset + i * 4;
                if (offset + 4 <= packet.Length - 2)
                {
                    byte[] floatBytes = new[] { packet[offset + 3], packet[offset + 2], packet[offset + 1], packet[offset] };
                    emgData.AbsMeanValues[i] = BitConverter.ToSingle(floatBytes, 0);
                }
            }

            DataReceived?.Invoke(this, emgData);
        }

        public async Task SendCommandAsync(byte categoryId, byte functionId, byte[]? data = null)
        {
            if (_stream == null || _client?.Connected != true)
            {
                throw new InvalidOperationException("Not connected to client.");
            }

            var packet = BuildPacket(categoryId, functionId, data);

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

            packet[0] = (byte)(Protocol.FrameHeader >> 8);
            packet[1] = (byte)(Protocol.FrameHeader & 0xFF);
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

            ushort crc = CalculateCrc16(packet, packetLength - 2);
            packet[packetLength - 2] = (byte)(crc >> 8);
            packet[packetLength - 1] = (byte)(crc & 0xFF);

            return packet;
        }

        private ushort CalculateCrc16(byte[] data, int length)
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

        public async Task SendHandshakeAsync()
        {
            await SendCommandAsync(Protocol.Category.BasicFunction, Protocol.Function.Handshake);
            LogMessage?.Invoke(this, "Sent handshake command.");
        }

        public async Task SendStartCollectionAsync()
        {
            await SendCommandAsync(Protocol.Category.ControlCommand, Protocol.Function.StartCollection);
            LogMessage?.Invoke(this, "Sent start collection command.");
        }

        public async Task SendStopCollectionAsync()
        {
            await SendCommandAsync(Protocol.Category.ControlCommand, Protocol.Function.StopCollection);
            LogMessage?.Invoke(this, "Sent stop collection command.");
        }
    }
}
