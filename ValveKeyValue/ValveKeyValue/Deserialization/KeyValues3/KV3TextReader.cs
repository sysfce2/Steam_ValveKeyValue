﻿using System;
using System.Globalization;
using System.IO;
using ValveKeyValue.Abstraction;

namespace ValveKeyValue.Deserialization.KeyValues3
{
    sealed class KV3TextReader : IVisitingReader
    {
        public KV3TextReader(TextReader textReader, IParsingVisitationListener listener, KVSerializerOptions options)
        {
            Require.NotNull(textReader, nameof(textReader));
            Require.NotNull(listener, nameof(listener));
            Require.NotNull(options, nameof(options));

            this.listener = listener;
            this.options = options;

            tokenReader = new KV3TokenReader(textReader, options);
            stateMachine = new KV3TextReaderStateMachine();
        }

        readonly IParsingVisitationListener listener;
        readonly KVSerializerOptions options;

        readonly KV3TokenReader tokenReader;
        readonly KV3TextReaderStateMachine stateMachine;
        bool disposed;

        public void ReadObject()
        {
            Require.NotDisposed(nameof(KV3TextReader), disposed);

            // TODO: Read header here as it's always expected, instead of using the tokenizer.

            while (stateMachine.IsInObject)
            {
                KVToken token;

                try
                {
                    token = tokenReader.ReadNextToken();
                }
                catch (InvalidDataException ex)
                {
                    throw new KeyValueException(ex.Message, ex);
                }
                catch (EndOfStreamException ex)
                {
                    throw new KeyValueException("Found end of file while trying to read token.", ex);
                }

                switch (token.TokenType)
                {
                    case KVTokenType.Header:
                        // TODO: Actually parse out the header
                        stateMachine.SetName("root"); // TODO: Get rid of this
                        break;

                    case KVTokenType.Assignment:
                        ReadAssignment();
                        break;

                    case KVTokenType.Identifier:
                        ReadIdentifier(token.Value);
                        break;

                    case KVTokenType.String:
                        ReadText(token.Value);
                        break;

                    case KVTokenType.ObjectStart:
                        BeginNewObject();
                        break;

                    case KVTokenType.ObjectEnd:
                        FinalizeCurrentObject(@explicit: true);
                        break;

                    case KVTokenType.ArrayStart:
                        BeginNewArray();
                        break;

                    case KVTokenType.ArrayEnd:
                        FinalizeCurrentArray();
                        break;

                    case KVTokenType.EndOfFile:
                        try
                        {
                            FinalizeDocument();
                        }
                        catch (InvalidOperationException ex)
                        {
                            throw new KeyValueException("Found end of file when another token type was expected.", ex);
                        }

                        break;

                    case KVTokenType.Comment:
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(token.TokenType), token.TokenType, "Unhandled token type.");
                }
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                tokenReader.Dispose();
                disposed = true;
            }
        }

        void ReadAssignment()
        {
            if (stateMachine.Current != KV3TextReaderState.InObjectBetweenKeyAndValue)
            {
                throw new InvalidOperationException($"Attempted to assign while in state {stateMachine.Current}.");
            }

            stateMachine.Push(KV3TextReaderState.InObjectBeforeValue);
        }

        void ReadIdentifier(string text)
        {
            switch (stateMachine.Current)
            {
                case KV3TextReaderState.InArray:
                    new KVObjectValue<string>(text, KVValueType.String);
                    break;

                // If we're after a value when we find more text, then we must be starting a new key/value pair.
                case KV3TextReaderState.InObjectAfterValue:
                    FinalizeCurrentObject(@explicit: false);
                    stateMachine.PushObject();
                    SetObjectKey(text);
                    break;

                case KV3TextReaderState.InObjectBeforeKey:
                    SetObjectKey(text);
                    break;

                case KV3TextReaderState.InObjectBeforeValue:
                    if (text.EndsWith(":") || text.EndsWith("+"))
                    {
                        // TODO: Parse flag like resource: then read as string
                    }
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled identifier reader state: {stateMachine.Current}.");
            }
        }

        void ReadText(string text)
        {
            KVValue value;

            switch (stateMachine.Current)
            {
                case KV3TextReaderState.InArray:
                    value = new KVObjectValue<string>(text, KVValueType.String);
                    listener.OnArrayValue(value);
                    break;

                case KV3TextReaderState.InObjectBeforeValue:
                    value = new KVObjectValue<string>(text, KVValueType.String);
                    var name = stateMachine.CurrentName;
                    listener.OnKeyValuePair(name, value);

                    stateMachine.Push(KV3TextReaderState.InObjectAfterValue);
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled text reader state: {stateMachine.Current}.");
            }
        }

        void BeginNewArray()
        {
            if (stateMachine.Current != KV3TextReaderState.InObjectBeforeValue)
            {
                throw new InvalidOperationException($"Attempted to begin new array while in state {stateMachine.Current}.");
            }

            stateMachine.PushObject();
            stateMachine.Push(KV3TextReaderState.InArray);
        }

        void FinalizeCurrentArray()
        {
            if (stateMachine.Current != KV3TextReaderState.InArray)
            {
                throw new InvalidOperationException($"Attempted to finalize array while in state {stateMachine.Current}.");
            }

            stateMachine.PopObject();

            if (stateMachine.IsInObject)
            {
                stateMachine.Push(KV3TextReaderState.InObjectAfterValue);
            }
        }

        void SetObjectKey(string name)
        {
            stateMachine.SetName(name);
            stateMachine.Push(KV3TextReaderState.InObjectBetweenKeyAndValue);
        }

        void BeginNewObject()
        {
            if (stateMachine.Current != KV3TextReaderState.Header && stateMachine.Current != KV3TextReaderState.InObjectBetweenKeyAndValue)
            {
                throw new InvalidOperationException($"Attempted to begin new object while in state {stateMachine.Current}.");
            }

            listener.OnObjectStart(stateMachine.CurrentName);

            stateMachine.PushObject();
            stateMachine.Push(KV3TextReaderState.InObjectBeforeKey);
        }

        void FinalizeCurrentObject(bool @explicit)
        {
            if (stateMachine.Current != KV3TextReaderState.InObjectBeforeKey && stateMachine.Current != KV3TextReaderState.InObjectAfterValue)
            {
                throw new InvalidOperationException($"Attempted to finalize object while in state {stateMachine.Current}.");
            }

            stateMachine.PopObject();

            if (stateMachine.IsInObject)
            {
                stateMachine.Push(KV3TextReaderState.InObjectAfterValue);
            }

            if (@explicit)
            {
                listener.OnObjectEnd();
            }
        }

        void FinalizeDocument()
        {
            FinalizeCurrentObject(@explicit: true);

            if (stateMachine.IsInObject)
            {
                throw new InvalidOperationException("Inconsistent state - at end of file whilst inside an object.");
            }
        }

        static KVValue ParseValue(string text)
        {
            // "0x" + 2 digits per byte. Long is 8 bytes, so s + 16 = 18.
            // Expressed this way for readability, rather than using a magic value.
            const int HexStringLengthForUnsignedLong = 2 + (sizeof(long) * 2);

            if (text.Length == HexStringLengthForUnsignedLong && text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                var hexadecimalString = text[2..];
                var data = ParseHexStringAsByteArray(hexadecimalString);

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(data);
                }

                var value = BitConverter.ToUInt64(data, 0);
                return new KVObjectValue<ulong>(value, KVValueType.UInt64);
            }

            const NumberStyles IntegerNumberStyles =
                NumberStyles.AllowLeadingWhite |
                NumberStyles.AllowLeadingSign;

            if (int.TryParse(text, IntegerNumberStyles, CultureInfo.InvariantCulture, out var intValue))
            {
                return new KVObjectValue<int>(intValue, KVValueType.Int32);
            }

            const NumberStyles FloatingPointNumberStyles =
                NumberStyles.AllowLeadingWhite |
                NumberStyles.AllowDecimalPoint |
                NumberStyles.AllowExponent |
                NumberStyles.AllowLeadingSign;

            if (float.TryParse(text, FloatingPointNumberStyles, CultureInfo.InvariantCulture, out var floatValue))
            {
                return new KVObjectValue<float>(floatValue, KVValueType.FloatingPoint);
            }

            return new KVObjectValue<string>(text, KVValueType.String);
        }

        static byte[] ParseHexStringAsByteArray(string hexadecimalRepresentation)
        {
            Require.NotNull(hexadecimalRepresentation, nameof(hexadecimalRepresentation));

            var data = new byte[hexadecimalRepresentation.Length / 2];
            for (var i = 0; i < data.Length; i++)
            {
                var currentByteText = hexadecimalRepresentation.Substring(i * 2, 2);
                data[i] = byte.Parse(currentByteText, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return data;
        }
    }
}