using System;
using System.IO;
using System.Security.Cryptography;

namespace Net._32Ba.LatticeDeformationTool
{
    // BinaryWriter's byte contract is preserved while SHA-256 receives bounded chunks.
    // The whole mesh payload never needs to exist as a managed byte array.
    internal sealed class CompatibilityHashStream : Stream
    {
        private readonly SHA256 _hash = SHA256.Create();
        private readonly byte[] _buffer = new byte[4096];
        private int _count;
        private bool _finished;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_finished;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_finished) throw new InvalidOperationException("Hash already finalized.");
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException();
            while (count > 0)
            {
                int take = Math.Min(count, _buffer.Length - _count);
                Buffer.BlockCopy(buffer, offset, _buffer, _count, take);
                _count += take; offset += take; count -= take;
                if (_count == _buffer.Length) Flush();
            }
        }
        public override void WriteByte(byte value)
        {
            if (_finished) throw new InvalidOperationException("Hash already finalized.");
            _buffer[_count++] = value;
            if (_count == _buffer.Length) Flush();
        }
        public override void Flush()
        {
            if (_count == 0) return;
            _hash.TransformBlock(_buffer, 0, _count, _buffer, 0);
            _count = 0;
        }
        internal byte[] Finish()
        {
            if (_finished) throw new InvalidOperationException("Hash already finalized.");
            Flush();
            _hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            _finished = true;
            return _hash.Hash;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { _finished = true; _hash.Dispose(); }
            base.Dispose(disposing);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
