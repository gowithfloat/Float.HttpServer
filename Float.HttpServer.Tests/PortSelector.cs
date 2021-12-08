namespace Float.HttpServer.Tests
{
    public static class PortSelector
    {
        const ushort defaultStartPort = 61550;
        static ushort index = 0;

        public static ushort SelectForAddress(string _, ushort startPort = defaultStartPort)
        {
            index += 1;
            return (ushort)(startPort + index);
        }
    }
}
