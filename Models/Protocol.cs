namespace EMGFeedbackSystem.Models
{
    public static class Protocol
    {
        // 帧头
        public const ushort FrameHeader = 0x1B07;
        
        // 地址信息
        public const byte SourceUpper = 0x01;
        public const byte SourceLower = 0x02;
        public const byte DestUpper = 0x01;
        public const byte DestLower = 0x02;
        
        // 数据长度定义
        public static class Length
        {
            public const ushort SampleData = 526;    // 上传采样数据：526字节 (2字节序号+2字节电压+512字节采样数据+10字节帧头)
            public const byte Handshake = 10;       // 握手指令：10字节
            public const byte HandshakeAck = 10;    // 握手应答：10字节
            public const byte StartCollection = 10; // 启动数据采集：10字节
            public const byte StartAck = 11;        // 应答启动状态：11字节
            public const byte StopCollection = 10;  // 停止数据采集：10字节
            public const byte StopAck = 11;         // 应答停止状态：11字节
        }

        // 数据包类型
        public static class Category
        {
            public const byte SampleData = 0x00;        // 上传采样数据
            public const byte BasicFunction = 0x01;     // 基本功能（握手）
            public const byte ControlCommand = 0x04;    // 控制命令（启动/停止）
        }

        // 功能码
        public static class Function
        {
            public const byte UploadSample = 0x01;     // 上传采样数据
            public const byte Handshake = 0x00;        // 握手指令
            public const byte HandshakeAck = 0x80;     // 握手应答
            public const byte StartCollection = 0x00;   // 启动数据采集
            public const byte StartAck = 0x80;         // 应答启动状态
            public const byte StopCollection = 0x01;    // 停止数据采集
            public const byte StopAck = 0x81;          // 应答停止状态
        }

        // 结果码
        public static class Result
        {
            public const byte Success = 0x01;          // 成功
            public const byte Failure = 0x02;          // 失败
        }
        
        // 时间间隔
        public const int DataIntervalMs = 100;       // 数据发送间隔：100ms
        
        // 电极通道定义
        public static class Channels
        {
            public const int ElectrodeA = 22;         // A电极通道数
            public const int ElectrodeB = 21;         // B电极通道数
            public const int ElectrodeC = 21;         // C电极通道数
            public const int Total = 64;              // 总通道数
        }
        
        // 数据格式
        public const int FloatBytes = 4;             // 浮点数字节数
        public const int PacketNumberBytes = 2;      // 数据包序号字节数
        public const int BatteryVoltageBytes = 2;    // 电池电压字节数
        public const int SampleDataBytes = 512;      // 采样数据字节数（64通道 * 4字节/通道 * 2组数据）
        public const int ChannelDataBytes = 4;       // 每通道数据字节数（4字节浮点数）
    }
}
