﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpRTSPServer
{
    /// <summary>
    /// AAC track.
    /// </summary>
    public class AACTrack : ITrack
    {
        /// <summary>
        /// Track ID.
        /// </summary>
        public int ID { get; set; } = 1;

        /// <summary>
        /// Sampling rate.
        /// </summary>
        public int SamplingRate { get; set; } = 44100;

        /// <summary>
        /// Number of channels.
        /// </summary>
        public int Channels { get; set; } = 1;

        /// <summary>
        /// AAC configuration descriptor.
        /// </summary>
        public byte[] ConfigDescriptor { get; set; }

        /// <summary>
        /// Is the track ready?
        /// </summary>
        public bool IsReady { get { return ConfigDescriptor != null && ConfigDescriptor.Length > 0; } }

        private int _payloadType = -1;

        /// <summary>
        /// Payload type. AAC uses a dynamic payload type, which by default we calculate as 96 + track ID.
        /// </summary>
        public int PayloadType
        {
            get
            {
                if (_payloadType < 0)
                {
                    return RTSPServer.DYNAMIC_PAYLOAD_TYPE + ID;
                }
                else
                {
                    return _payloadType;
                }
            }
            set
            {
                _payloadType = value;
            }
        }

        /// <summary>
        /// Ctor.
        /// </summary>
        /// <param name="samplingRate">Audio sampling rate.</param>
        /// <param name="channels">Number of audio channels.</param>
        public AACTrack(int samplingRate, int channels)
        {
            this.SamplingRate = samplingRate;
            this.Channels = channels;
        }

        /// <summary>
        /// Ctor.
        /// </summary>
        /// <param name="configDescriptor">AAC configuration descriptor.</param>
        /// <param name="samplingRate">Audio sampling rate.</param>
        /// <param name="channels">Number of audio channels.</param>
        public AACTrack(byte[] configDescriptor, int samplingRate, int channels) : this(samplingRate, channels)
        {
            SetConfigDescriptor(configDescriptor);
        }

        /// <summary>
        /// Set the AAC configuration Descriptor.
        /// </summary>
        /// <param name="configDescriptor">AAC Configuration Descriptor.</param>
        public void SetConfigDescriptor(byte[] configDescriptor)
        {
            this.ConfigDescriptor = configDescriptor;
        }

        public StringBuilder BuildSDP(StringBuilder sdp)
        {
            sdp.Append($"m=audio 0 RTP/AVP {PayloadType}\n"); // <---- Payload Type 0 means G711 ULAW, 96+ means dynamic payload type
            sdp.Append($"a=control:trackID={ID}\n");
            sdp.Append($"a=rtpmap:{PayloadType} mpeg4-generic/{SamplingRate}/{Channels}\n");
            sdp.Append($"a=fmtp:{PayloadType} profile-level-id={GetAACProfileLevel(SamplingRate, Channels)}; " +
                $"config={Utilities.ToHexString(ConfigDescriptor)}; streamType=5; mode=AAC-hbr; objectType=64; sizeLength=13; indexLength=3; indexDeltaLength=3\n");
            return sdp;
        }

        public (List<Memory<byte>>, List<IMemoryOwner<byte>>) CreateRtpPackets(List<byte[]> samples, uint rtpTimestamp)
        {
            List<Memory<byte>> rtpPackets = new List<Memory<byte>>();
            List<IMemoryOwner<byte>> memoryOwners = new List<IMemoryOwner<byte>>();

            for (int i = 0; i < samples.Count; i++)
            {
                // append AU header (required for AAC)
                var audioPacket = AppendAUHeader(samples[i]);

                // Put the whole Audio Packet into one RTP packet.
                // 12 is header size when there are no CSRCs or extensions
                var size = 12 + audioPacket.Length;
                var owner = MemoryPool<byte>.Shared.Rent(size);
                memoryOwners.Add(owner);

                var rtpPacket = owner.Memory.Slice(0, size);

                const bool rtpPadding = false;
                const bool rtpHasExtension = false;
                int rtpCsrcCount = 0;
                const bool rtpMarker = true; // always 1 as this is the last (and only) RTP packet for this audio timestamp

                RTPPacketUtil.WriteHeader(rtpPacket.Span,
                    RTPPacketUtil.RTP_VERSION, rtpPadding, rtpHasExtension, rtpCsrcCount, rtpMarker, PayloadType);

                // sequence number is set just before send
                RTPPacketUtil.WriteTS(rtpPacket.Span, rtpTimestamp);

                // Now append the audio packet
                audioPacket.CopyTo(rtpPacket.Slice(12));

                rtpPackets.Add(rtpPacket);
            }

            return (rtpPackets, memoryOwners);
        }

        private static byte[] AppendAUHeader(byte[] frame)
        {
            short frameLen = (short)(frame.Length << 3);
            byte[] header = new byte[4];
            header[0] = 0x00;
            header[1] = 0x10; // 16 bits size of the header
            header[2] = (byte)((frameLen >> 8) & 0xFF);
            header[3] = (byte)(frameLen & 0xFF);
            return header.Concat(frame).ToArray();
        }

        private static int GetAACLevel(int samplingFrequency, int channelConfiguration)
        {
            if (samplingFrequency <= 24000)
            {
                if (channelConfiguration <= 2)
                    return 1; // AAC Profile, Level 1
            }
            else if (samplingFrequency <= 48000)
            {
                if (channelConfiguration <= 2)
                    return 2; // Level 2
                else if (channelConfiguration <= 5)
                    return 4; // Level 4
            }
            else if (samplingFrequency <= 96000)
            {
                if (channelConfiguration <= 5)
                    return 5; // Level 5
            }

            return 5;
        }

        private static int GetAACHELevel(int samplingFrequency, int channelConfiguration, bool sbr)
        {
            if (samplingFrequency <= 48000)
            {
                if (channelConfiguration <= 2)
                    return sbr ? 3 : 2; // Level 2/3
                else if (channelConfiguration <= 5)
                    return 4; // Level 4
            }
            else if (samplingFrequency <= 96000)
            {
                if (channelConfiguration <= 5)
                    return 5; // Level 5
            }

            return 5;
        }

        private static int GetAACHQLevel(int samplingFrequency, int channelConfiguration)
        {
            if (samplingFrequency <= 22050)
            {
                if (channelConfiguration <= 2)
                    return 1; // Level 1/5
            }
            else if (samplingFrequency <= 48000)
            {
                if (channelConfiguration <= 2)
                    return 2; // Level 2/6
                else if (channelConfiguration <= 5)
                    return 3; // Level 3/4/7/8
            }

            return 8;
        }

        private static int GetAACProfileLevel(int samplingFrequency, int channelConfiguration, int profile = 2, bool sbr = false)
        {
            switch (profile)
            {
                case 2: // AAC_LC
                    return GetAACLevel(samplingFrequency, channelConfiguration) - 1 + 0x28;

                case 5: // AAC_SBR
                    return GetAACHELevel(samplingFrequency, channelConfiguration, sbr) - 2 + 0x2C;

                case 29: // AAC_PS
                    return GetAACHELevel(samplingFrequency, channelConfiguration, sbr) - 2 + 0x30;

                case 8: // AAC_CELP
                    return GetAACHQLevel(samplingFrequency, channelConfiguration) - 1 + 0x0E;

                default:
                    return 1;
            }
        }
    }
}
