using System;
using System.IO;

namespace ContentServerUploader
{
    internal class ProgressStream : Stream
    {
        private readonly Stream _file;
        private readonly long _length;
        private long _bytesRead;

        public class ProgressChangedEventArgs : EventArgs
        {
            public long BytesRead;
            public long Length;
   
            public ProgressChangedEventArgs(long bytesRead, long length)
            {
                BytesRead = bytesRead;
                Length = length;
            }
        }

        public event EventHandler<ProgressChangedEventArgs> ProgressChanged;
          

        public ProgressStream(Stream file)
        {
            _file = file;
            _length = file.Length;
            _bytesRead = 0;
            if (ProgressChanged != null) ProgressChanged(this, new ProgressChangedEventArgs(_bytesRead, _length));
        }

        public double GetProgress()
        {
            return ((double)_bytesRead) / _file.Length;
        }
        public override void Flush()
        {            
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int result = _file.Read(buffer, offset, count);
            _bytesRead += result;
            if (ProgressChanged != null) ProgressChanged(this, new ProgressChangedEventArgs(_bytesRead, _length));
            return result;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override long Length
        {
            get { return _file.Length; }
        }

        public override long Position {
            get { return _bytesRead; }
            set { throw new Exception("The operation is not implemented.");}
        }
    }
}