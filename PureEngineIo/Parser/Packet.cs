﻿using PureEngineIo.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace PureEngineIo.Parser
{
	public class Packet
	{
		public static readonly string OPEN = "open";
		public static readonly string CLOSE = "close";
		public static readonly string PING = "ping";
		public static readonly string PONG = "pong";
		public static readonly string UPGRADE = "upgrade";
		public static readonly string MESSAGE = "message";
		public static readonly string NOOP = "noop";
		public static readonly string ERROR = "error";

		private static readonly int MAX_INT_CHAR_LENGTH = int.MaxValue.ToString().Length;

		//TODO: suport binary?
		private bool _supportsBinary;

		private static readonly Dictionary<string, byte> _packets = new Dictionary<string, byte>()
		{
			{OPEN, 0},
			{CLOSE, 1},
			{PING, 2},
			{PONG, 3},
			{MESSAGE, 4},
			{UPGRADE, 5},
			{NOOP, 6}
		};

		private static readonly Dictionary<byte, string> _packetsList = new Dictionary<byte, string>();

		static Packet()
		{
			foreach (var entry in _packets)
			{
				_packetsList.Add(entry.Value, entry.Key);
			}
		}

		private static readonly Packet _err = new Packet(ERROR, "parser error");

		public string Type { get; set; }
		public object Data { get; set; }

		public Packet(string type, object data)
		{
			Type = type;
			Data = data;
		}

		public Packet(string type)
		{
			Type = type;
			Data = null;
		}

		internal void Encode(IEncodeCallback callback, bool utf8encode = false)
		{
			if (Data is byte[])
			{
				if (!_supportsBinary)
				{
					EncodeBase64Packet(callback);
					return;
				}
				EncodeByteArray(callback);
				return;
			}
			var encodedStringBuilder = new StringBuilder();
			encodedStringBuilder.Append(_packets[Type]);

			if (Data != null)
			{
				encodedStringBuilder.Append(utf8encode ? UTF8.Encode((string)Data) : (string)Data);
			}

			callback.Call(encodedStringBuilder.ToString());
		}

		private void EncodeBase64Packet(IEncodeCallback callback)
		{
			if (Data is byte[] byteData)
			{
				var result = new StringBuilder();
				result.Append("b");
				result.Append(_packets[Type]);
				result.Append(Convert.ToBase64String(byteData));
				callback.Call(result.ToString());
				return;
			}
			throw new Exception("byteData == null");
		}

		private void EncodeByteArray(IEncodeCallback callback)
		{
			if (Data is byte[] byteData)
			{
				var resultArray = new byte[1 + byteData.Length];
				resultArray[0] = _packets[Type];
				Array.Copy(byteData, 0, resultArray, 1, byteData.Length);
				callback.Call(resultArray);
				return;
			}
			throw new Exception("byteData == null");
		}

		internal static Packet DecodePacket(string data, bool utf8decode = false)
		{
			if (data.StartsWith("b"))
			{
				return DecodeBase64Packet(data.Substring(1));
			}

			var s = data.Substring(0, 1);
			if (!int.TryParse(s, out int type))
			{
				type = -1;
			}

			if (utf8decode)
			{
				try
				{
					data = UTF8.Decode(data);
				}
				catch (Exception)
				{
					return _err;
				}
			}

			if (type < 0 || type >= _packetsList.Count)
			{
				return _err;
			}

			return data.Length > 1 ? new Packet(_packetsList[(byte)type], data.Substring(1)) : new Packet(_packetsList[(byte)type], null);
		}

		private static Packet DecodeBase64Packet(string msg)
		{
			var s = msg.Substring(0, 1);
			if (!int.TryParse(s, out var type))
			{
				type = -1;
			}
			if (type < 0 || type >= _packetsList.Count)
			{
				return _err;
			}
			msg = msg.Substring(1);
			var decodedFromBase64 = Convert.FromBase64String(msg);
			return new Packet(_packetsList[(byte)type], decodedFromBase64);
		}

		internal static Packet DecodePacket(byte[] data)
		{
			int type = data[0];
			var byteArray = new byte[data.Length - 1];
			Array.Copy(data, 1, byteArray, 0, byteArray.Length);
			return new Packet(_packetsList[(byte)type], byteArray);
		}

		internal static void EncodePayload(Packet[] packets, IEncodeCallback callback)
		{
			if (packets.Length == 0)
			{
				callback.Call(new byte[0]);
				return;
			}

			var results = new List<byte[]>(packets.Length);
			var encodePayloadCallback = new EncodePayloadCallback(results);
			foreach (var packet in packets)
			{
				packet.Encode(encodePayloadCallback, true);
			}

			callback.Call(Buffer.Concat(results.ToArray()));//new byte[results.Count][]
		}

		/// <summary>
		/// Decodes data when a payload is maybe expected.
		/// </summary>
		/// <param name="data"></param>
		/// <param name="callback"></param>
		public static void DecodePayload(string data, IDecodePayloadCallback callback)
		{
			if (string.IsNullOrEmpty(data))
			{
				callback.Call(_err, 0, 1);
				return;
			}

			var length = new StringBuilder();
			for (int i = 0, l = data.Length; i < l; i++)
			{
				var chr = Convert.ToChar(data.Substring(i, 1));

				if (chr != ':')
				{
					length.Append(chr);
				}
				else
				{
					if (!int.TryParse(length.ToString(), out int n))
					{
						callback.Call(_err, 0, 1);
						return;
					}

					string msg;
					try
					{
						msg = data.Substring(i + 1, n);
					}
					catch (ArgumentOutOfRangeException)
					{
						callback.Call(_err, 0, 1);
						return;
					}

					if (msg.Length != 0)
					{
						var packet = DecodePacket(msg, true);
						if (_err.Type == packet.Type && _err.Data == packet.Data)
						{
							callback.Call(_err, 0, 1);
							return;
						}

						var ret = callback.Call(packet, i + n, l);
						if (!ret)
						{
							return;
						}
					}

					i += n;
					length = new StringBuilder();
				}
			}
			if (length.Length > 0)
			{
				callback.Call(_err, 0, 1);
			}
		}

		/// <summary>
		/// Decodes data when a payload is maybe expected.
		/// </summary>
		/// <param name="data"></param>
		/// <param name="callback"></param>
		public static void DecodePayload(byte[] data, IDecodePayloadCallback callback)
		{
			var bufferTail = ByteBuffer.Wrap(data);
			var buffers = new List<object>();
			var bufferTailOffset = 0;

			while (bufferTail.Capacity - bufferTailOffset > 0)
			{
				var strLen = new StringBuilder();
				var isString = (bufferTail.Get(0 + bufferTailOffset) & 0xFF) == 0;
				var numberTooLong = false;

				for (var i = 1; ; i++)
				{
					var b = bufferTail.Get(i + bufferTailOffset) & 0xFF;
					if (b == 255)
						break;

					// support only integer
					if (strLen.Length > MAX_INT_CHAR_LENGTH)
					{
						numberTooLong = true;
						break;
					}
					strLen.Append(b);
				}
				if (numberTooLong)
				{
					callback.Call(_err, 0, 1);
					return;
				}

				bufferTailOffset += strLen.Length + 1;

				var msgLength = int.Parse(strLen.ToString());

				bufferTail.Position(1 + bufferTailOffset);
				bufferTail.Limit(msgLength + 1 + bufferTailOffset);

				bufferTail.Get(new byte[bufferTail.Remaining()], 0, (new byte[bufferTail.Remaining()]).Length);

				if (isString)
				{
					buffers.Add(Helpers.ByteArrayToString(new byte[bufferTail.Remaining()]));
				}
				else
				{
					buffers.Add(new byte[bufferTail.Remaining()]);
				}
				bufferTail.Clear();
				bufferTail.Position(msgLength + 1 + bufferTailOffset);
				bufferTailOffset += msgLength + 1;
			}

			var total = buffers.Count;
			for (var i = 0; i < total; i++)
			{
				var buffer = buffers[i];
				if (buffer is string s)
				{
					callback.Call(DecodePacket(s, true), i, total);
				}
				else if (buffer is byte[] bytes)
				{
					callback.Call(DecodePacket(bytes), i, total);
				}
			}
		}
	}
}
