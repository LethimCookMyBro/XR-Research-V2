#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    internal sealed class ResearchS1GameViewVideoWriter
    {
        internal sealed class Frame
        {
            internal readonly int Index, Width, Height;
            internal readonly byte[] Pixels;
            internal readonly string Receipt;
            internal Frame(int index, int width, int height, byte[] pixels, string receipt)
            { Index = index; Width = width; Height = height; Pixels = pixels; Receipt = receipt; }
        }

        internal const int QueueCapacity = 2;
        readonly BlockingCollection<Frame> frames = new BlockingCollection<Frame>(QueueCapacity);
        readonly Thread worker;
        readonly string output;
        readonly Func<byte[], int, int, byte[]> encode;
        readonly long byteLimit;
        volatile string error;
        volatile bool drained, closed;
        int written;
        long bytes;
        internal string Error => error;
        internal bool Drained => drained;
        internal int Written => Volatile.Read(ref written);
        internal long Bytes => Interlocked.Read(ref bytes);
        internal int Pending => closed ? 0 : frames.Count;

        internal ResearchS1GameViewVideoWriter(string directory, Func<byte[], int, int, byte[]> encoder,
            long limit = 2L * 1024 * 1024 * 1024)
        {
            output = directory;
            encode = encoder ?? throw new ArgumentNullException(nameof(encoder));
            if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
            byteLimit = limit;
            Directory.CreateDirectory(Path.Combine(output, "frames"));
            worker = new Thread(Write) { IsBackground = true, Name = "S1 GameView evidence writer" };
            worker.Start();
        }

        internal static void ReverseRgbaRows(byte[] pixels, int width, int height, bool reverseRows)
        {
            if (width <= 0 || height <= 0 || (long)width * height > 1920L * 1080 || pixels == null ||
                pixels.LongLength != (long)width * height * 4)
                throw new ArgumentException("Expected an owned RGBA32 rendered frame.");
            if (!reverseRows || height < 2) return;
            int rowBytes = width * 4;
            var row = new byte[rowBytes];
            for (int y = 0; y < height / 2; y++)
            {
                int top = y * rowBytes, bottom = (height - 1 - y) * rowBytes;
                Buffer.BlockCopy(pixels, top, row, 0, rowBytes);
                Buffer.BlockCopy(pixels, bottom, pixels, top, rowBytes);
                Buffer.BlockCopy(row, 0, pixels, bottom, rowBytes);
            }
        }

        internal bool TryAdd(Frame frame)
        {
            if (frame == null || frame.Index <= 0 || frame.Width <= 0 || frame.Height <= 0 ||
                (long)frame.Width * frame.Height > 1920L * 1080 || frame.Pixels == null ||
                frame.Pixels.LongLength != (long)frame.Width * frame.Height * 4 || string.IsNullOrEmpty(frame.Receipt))
                throw new ArgumentException("Expected an immutable RGBA32 rendered-frame receipt.");
            if (closed || error != null || frames.IsAddingCompleted) return false;
            return frames.TryAdd(frame);
        }

        internal bool Close()
        {
            if (closed) return drained;
            if (!frames.IsAddingCompleted) frames.CompleteAdding();
            bool joined = worker.Join(5000);
            if (!joined && error == null) error = "Writer did not quiesce within five seconds. Cleanup is not verified.";
            if (joined) { closed = true; frames.Dispose(); }
            return joined && drained;
        }

        void Write()
        {
            try
            {
                using (var stream = new FileStream(Path.Combine(output, "frames.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var ledger = new StreamWriter(stream, new UTF8Encoding(false)))
                using (var sha = SHA256.Create())
                    foreach (var frame in frames.GetConsumingEnumerable())
                    {
                        var png = encode(frame.Pixels, frame.Width, frame.Height);
                        if (png == null || png.Length == 0) throw new InvalidDataException("PNG encoder produced no bytes.");
                        if (png.LongLength > byteLimit - Bytes) throw new IOException("Capture reached the explicit PNG byte limit.");
                        string file = "frame_" + frame.Index.ToString("D8", CultureInfo.InvariantCulture) + ".png";
                        using (var image = new FileStream(Path.Combine(output, "frames", file), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                            image.Write(png, 0, png.Length);
                        string hash = BitConverter.ToString(sha.ComputeHash(png)).Replace("-", "").ToLowerInvariant();
                        ledger.WriteLine("{\"frame\":" + frame.Receipt + ",\"png_file\":\"frames/" + file +
                            "\",\"sha256\":\"" + hash + "\",\"bytes\":" + png.Length.ToString(CultureInfo.InvariantCulture) + "}");
                        ledger.Flush();
                        Interlocked.Add(ref bytes, png.LongLength);
                        Interlocked.Increment(ref written);
                    }
            }
            catch (Exception failure) { error = failure.ToString(); }
            finally { drained = true; }
        }
    }
}
#endif
