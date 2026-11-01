using System.Text;

internal static class AudioFixture
{
    // PCM 全零，样本本身无声；播放器检查仍必须从创建起保持静音和零音量。
    internal static void Write(string path, int seconds = 8)
    {
        const int rate = 16000;
        int bytes = rate * seconds * 2;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
        writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(bytes); writer.Write(new byte[bytes]);
    }
}
