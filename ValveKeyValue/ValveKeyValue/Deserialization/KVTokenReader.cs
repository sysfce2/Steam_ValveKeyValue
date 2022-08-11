using System;
using System.IO;

namespace ValveKeyValue.Deserialization
{
    abstract class KVTokenReader : IDisposable
    {
        public KVTokenReader(TextReader textReader)
        {
            Require.NotNull(textReader, nameof(textReader));

            this.textReader = textReader;
        }

        protected TextReader textReader;
        protected int? peekedNext;
        protected bool disposed;

        public void Dispose()
        {
            if (!disposed)
            {
                textReader.Dispose();
                textReader = null;

                disposed = true;
            }
        }

        protected char Next()
        {
            int next;

            if (peekedNext.HasValue)
            {
                next = peekedNext.Value;
                peekedNext = null;
            }
            else
            {
                next = textReader.Read();
            }

            if (IsEndOfFile(next))
            {
                throw new EndOfStreamException();
            }

            return (char)next;
        }

        protected int Peek()
        {
            if (peekedNext.HasValue)
            {
                return peekedNext.Value;
            }

            var next = textReader.Read();
            peekedNext = next;

            return next;
        }

        protected void ReadChar(char expectedChar)
        {
            var next = Next();
            if (next != expectedChar)
            {
                throw new InvalidDataException($"The syntax is incorrect, expected '{expectedChar}' but got '{next}'.");
            }
        }

        protected void SwallowWhitespace()
        {
            while (PeekWhitespace())
            {
                Next();
            }
        }

        protected bool PeekWhitespace()
        {
            var next = Peek();
            return !IsEndOfFile(next) && char.IsWhiteSpace((char)next);
        }

        protected bool IsEndOfFile(int value) => value == -1;
    }
}
