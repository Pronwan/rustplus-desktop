using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace RustPlusDesk.Services
{
    public class A2SPlayer
    {
        public string Name { get; set; } = "";
        public float Duration { get; set; }
    }

    public static class A2SClient
    {
        public static async Task<List<A2SPlayer>> QueryPlayersAsync(string host, int port, int timeoutMs = 3000)
        {
            IPAddress ip;
            if (!IPAddress.TryParse(host, out ip))
            {
                var addrs = await Dns.GetHostAddressesAsync(host);
                if (addrs.Length == 0) throw new Exception("Could not resolve host.");
                ip = addrs[0];
            }

            using var udp = new UdpClient();
            var ep = new IPEndPoint(ip, port);
            udp.Connect(ep);

            using var cts = new CancellationTokenSource(timeoutMs);

            // 1. Send initial A2S_PLAYER request with dummy challenge 0xFFFFFFFF
            byte[] req = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x55, 0xFF, 0xFF, 0xFF, 0xFF };
            await udp.SendAsync(req, req.Length);

            // 2. Read the first response — could be:
            //    0x41  A2S_CHALLENGE: server wants us to re-send with a real token
            //    0x44  A2S_PLAYER:    server skipped the challenge and sent the list directly (valid per spec)
            var firstRes = await udp.ReceiveAsync().WithCancellation(cts.Token);
            {
                using var ms0 = new MemoryStream(firstRes.Buffer);
                using var br0 = new BinaryReader(ms0);
                if (ms0.Length >= 5)
                {
                    uint h0 = br0.ReadUInt32();
                    if (h0 == 0xFFFFFFFF)
                    {
                        byte cmd0 = br0.ReadByte();
                        if (cmd0 == 0x44) // Direct player list — no challenge required by this server
                            return ParsePlayers(br0);

                        if (cmd0 == 0x41) // Challenge: re-send with the real token
                        {
                            byte[] challenge = br0.ReadBytes(4);
                            var req2 = new List<byte> { 0xFF, 0xFF, 0xFF, 0xFF, 0x55 };
                            req2.AddRange(challenge);
                            await udp.SendAsync(req2.ToArray(), req2.Count);
                            // fall through to multi-packet receive below
                        }
                    }
                }
            }

            // 3. Receive player list (may be multi-packet / split)
            var packets = new Dictionary<int, byte[]>();
            int totalPackets = -1;
            uint packetId = 0;

            while (!cts.IsCancellationRequested)
            {
                var pResTask = udp.ReceiveAsync();
                if (await Task.WhenAny(pResTask, Task.Delay(timeoutMs, cts.Token)) != pResTask)
                {
                    throw new Exception($"Timeout waiting for player packets on port {port}. Received {packets.Count}/{totalPackets} split packets.");
                }

                var pRes = await pResTask;
                var pMs = new MemoryStream(pRes.Buffer);
                var pBr = new BinaryReader(pMs);

                uint pHeader = pBr.ReadUInt32();
                if (pHeader == 0xFFFFFFFF) // Single packet
                {
                    byte cmd = pBr.ReadByte();
                    if (cmd == 0x44)
                    {
                        return ParsePlayers(pBr);
                    }
                    else
                    {
                        Debug.WriteLine($"[A2S] Unexpected single packet cmd: 0x{cmd:X2}");
                    }
                }
                else if (pHeader == 0xFFFFFFFE) // Split packet
                {
                    uint id = pBr.ReadUInt32();
                    byte total = pBr.ReadByte();
                    byte num = pBr.ReadByte();
                    ushort size = pBr.ReadUInt16();
                    
                    Debug.WriteLine($"[A2S] Split packet received: ID={id}, Total={total}, Num={num}, Size={size}");

                    if (totalPackets == -1)
                    {
                        totalPackets = total;
                        packetId = id;
                    }

                    if (id == packetId)
                    {
                        byte[] payload = pBr.ReadBytes((int)(pMs.Length - pMs.Position));
                        packets[num] = payload;
                    }

                    if (packets.Count == totalPackets)
                    {
                        Debug.WriteLine($"[A2S] Reassembling {totalPackets} packets...");
                        var combined = new MemoryStream();
                        for (int i = 0; i < totalPackets; i++)
                        {
                            if (packets.ContainsKey(i))
                                combined.Write(packets[i], 0, packets[i].Length);
                        }
                        combined.Position = 0;

                        // Check if compressed
                        if ((packetId & 0x80000000) != 0)
                        {
                            throw new Exception("BZIP2 Compression not supported.");
                        }

                        var cBr = new BinaryReader(combined);
                        uint cHeader = cBr.ReadUInt32();
                        if (cHeader == 0xFFFFFFFF)
                        {
                            byte cmd = cBr.ReadByte();
                            if (cmd == 0x44)
                                return ParsePlayers(cBr);
                        }
                        else
                        {
                            throw new Exception("Invalid header in reassembled packet: " + cHeader);
                        }
                    }
                }
            }

            throw new Exception("Timeout waiting for full player list.");
        }

        private static List<A2SPlayer> ParsePlayers(BinaryReader br)
        {
            var list = new List<A2SPlayer>();
            if (br.BaseStream.Position >= br.BaseStream.Length) return list;
            
            byte count = br.ReadByte();
            for (int i = 0; i < count; i++)
            {
                if (br.BaseStream.Position >= br.BaseStream.Length) break;
                br.ReadByte(); // Index
                string name = ReadNullTerminatedString(br);
                
                if (br.BaseStream.Position + 8 > br.BaseStream.Length) break; // Needs 4 bytes score + 4 bytes duration
                
                long score = br.ReadInt32();
                float duration = br.ReadSingle();
                list.Add(new A2SPlayer { Name = name, Duration = duration });
            }
            return list;
        }

        private static string ReadNullTerminatedString(BinaryReader br)
        {
            var bytes = new List<byte>();
            while (br.BaseStream.Position < br.BaseStream.Length)
            {
                byte b = br.ReadByte();
                if (b == 0) break;
                bytes.Add(b);
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }

    public static class TaskExtensions
    {
        public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s!).TrySetResult(true), tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
            return await task;
        }
    }
}
