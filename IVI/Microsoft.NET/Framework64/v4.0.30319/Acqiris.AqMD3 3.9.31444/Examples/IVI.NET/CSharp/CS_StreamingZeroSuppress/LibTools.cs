using Acqiris.AqMD3;
using System;
using System.Linq;
using System.Collections.Generic;

namespace LibTools
{
    class Tools
    {
        /// <summary>
        /// Align given "value" to the next multiple of "divider"
        /// </summary>
        public static Int64 AlignUp(Int64 value, Int32 divider)
        { return (value % divider == 0) ? value : ((value / divider) + 1) * divider; }

        /// <summary>
        /// Extracts a block index from two 32-bit elements.
        /// </summary>
        public static Int64 ExtractBlockIndex(Int32 element0, Int32 element1)
        {
            Int64 low = (element0 >> 24) & 0xff;
            Int64 high = ((Int64)(element1 & 0xffffff)) << 8;
            return high | low;
        }
    }

    /// <summary>
    /// Gathers all parameters required to decode markers and to unpack gated records.
    /// </summary>
    class ProcessingParameters
    {
        public ProcessingParameters(int storageSamples, int processingSamples, double tsPeriod, int preGate, int postGate)
        {
            StorageBlockSamples = storageSamples;
            ProcessingBlockSamples = processingSamples;
            TimestampingPeriod = tsPeriod;
            PreGateSamples = preGate;
            PostGateSamples = postGate;
        }

        public readonly int StorageBlockSamples;        // memory-storage alignment constraint. Expressed in 16-bit samples.
        public readonly int ProcessingBlockSamples;     // processing alignment constraint. Expressed in 16-bit samples.
        public readonly double TimestampingPeriod;      // timestamping period. Expressed in seconds.
        public readonly int PreGateSamples;             // applied pre-gate samples.
        public readonly int PostGateSamples;            // applied post-gate samples.
    }

    enum MarkerTag
    {
        None            = 0x00,
        TriggerNormal   = 0x01, // 512-bit: Trigger marker in standard Normal acquisition mode
        TriggerAverager = 0x02, // 512-bit: Trigger marker in Averager acquisition mode
        GateStart       = 0x04, //  64-bit: Gate start marker.
        GateStop        = 0x05, //  64-bit: Gate stop marker.
        DummyGate       = 0x08, //  64-bit: Dummy gate marker.
        RecordStop      = 0x0a, //  64-bit: Record stop marker.
    };

    /// <summary>
    /// Represents a trigger marker
    /// </summary>
    class TriggerMarker
    {
        /// <summary>
        /// Constructs a trigger marker object from a raw-marker.
        /// </summary>
        /// <param name="element0">First 32-bit element of the trigger marker packet.</param>
        /// <param name="element1">Second 32-bit element of the trigger marker packet.</param>
        /// <param name="element2">Third 32-bit element of the trigger marker packet.</param>
        public TriggerMarker(Int32 element0, Int32 element1, Int32 element2)
        {
            MarkerTag tag = (MarkerTag)(element0 & 0xff);

            if (tag != MarkerTag.TriggerNormal && tag != MarkerTag.TriggerAverager)
                throw new ArgumentException(String.Format("Expected trigger marker, got {0}" , tag));

            Tag = tag;
            RecordIndex = (UInt32)((element0 >> 8) & TriggerMarker.RecordIndexMask);

            TriggerSubSample = -((double)(element1 & 0x000000ff) / 256.0);
            UInt64 timestampLow = (UInt64)(element1 >> 8) & 0x0000000000ffffffL;
            UInt64 timestampHigh = (UInt64)(element2) << 24;
            AbsoluteTriggerSampleIndex = timestampHigh | timestampLow;
        }

        /// <summary>
        /// Return the absolute time of trigger (since init of the module) in seconds.
        /// </summary>
        /// <param name="timestampPeriod">Represents timestamping period.</param>
        /// <returns>Trigger time in seconds</returns>
        public double GetTriggerTime(double timestampPeriod)
        { return (AbsoluteTriggerSampleIndex + TriggerSubSample) * timestampPeriod; }

        /// <summary>
        /// Return the timespan (in seconds) between the trigger event, and the first sample of the record.
        /// </summary>
        /// <param name="samplePeriod">Represents sampling period in seconds.</param>
        /// <param name="triggerDelay">Represents trigger delay in seconds.</param>
        /// <returns>Timespan from trigger event to first sample of the record in seconds</returns>
        public double GetStartTime(double samplePeriod, double triggerDelay = 0.0)
        { return triggerDelay + TriggerSubSample * samplePeriod; }

        public const Int32 RecordIndexMask = 0x00ffffff;

        public readonly MarkerTag Tag;                      // the tag of trigger marker.
        public readonly UInt64 AbsoluteTriggerSampleIndex;  // The sample index where the trigger event occured.
        public readonly double TriggerSubSample;            // the subsample part of the trigger event.
        public readonly UInt32 RecordIndex;                 // index of the record
    }


    /// <summary>
    /// Represent a gate-start marker
    /// </summary>
    class GateStartMarker
    {
        /// <summary>
        /// Constructs a trigger marker object from a 64-bit raw-marker.
        /// </summary>
        /// <param name="element0">First 32-bit element of the gate-start marker packet.</param>
        /// <param name="element1">Second 32-bit element of the gate-start marker packet.</param
        public GateStartMarker(Int32 element0, Int32 element1)
        {
            MarkerTag tag = (MarkerTag)(element0 & 0xff);
            if (tag != MarkerTag.GateStart)
                throw new ArgumentException(String.Format("Expected gate start tag, got {0}", tag));

            var blockIndex = Tools.ExtractBlockIndex(element0, element1);
            if (blockIndex < 1)
                throw new ArgumentException(String.Format("Start block index must be strict positive, got {0} (marker[0] = 0x{1:X8}, marker[1]=0x{2:X8})", blockIndex, element0, element1));

            BlockIndex = blockIndex;
            StartSampleIndex = (element1 >> 24) & 0xff;
        }

        /// <summary>
        /// Return the number of the leading suppressed samples in the first block of the gate.
        /// </summary>
        public Int64 SuppressedSampleCount { get { return StartSampleIndex; } }

        /// <summary>
        /// Return the index (in the record) of the sample where the gate start condition is met.
        /// It is the very first sample which goes above the configured zero-suppress threshold.
        /// </summary>
        public Int64 GetStartSampleIndex(ProcessingParameters processingParams)
        { return (Int64)(BlockIndex - 1) * processingParams.ProcessingBlockSamples + SuppressedSampleCount;}


        public readonly Int64 BlockIndex;           // The block index where the gate-start condition is met. Expressed in blocks of "processingParams.ProcessingBlockSamples"
        public readonly Int32 StartSampleIndex;     // The index of the first sample of the gate(the one above the threshold).
    }

    /// <summary>
    /// Represent a stop marker, either gate-stop marker or record-stop marker.
    /// </summary>
    class StopMarker
    {
        /// <summary>
        /// Constructs stop marker object from a raw 64-bit marker. The 64-bit raw marker is represented by two 32-bit elements (element0 & element1).
        /// </summary>
        /// <param name="element0">The least significant part of the raw marker.</param>
        /// <param name="element1">The most significant part of the raw marker.</param>
        public StopMarker(Int32 element0, Int32 element1)
        {
            MarkerTag tag = (MarkerTag)(element0 & 0xff);

            if (tag != MarkerTag.GateStop && tag != MarkerTag.RecordStop)
                throw new ArgumentException(String.Format("Expected gate stop or record stop tags from header, got {0}", tag));

            Int64 blockIndex = Tools.ExtractBlockIndex(element0, element1);
            if (blockIndex < 1)
                throw new ArgumentException(String.Format("Stop block index must be strict positive, got {0} (marker[0] = 0x{1:X8}, marker[1]=0x{2:X8})", blockIndex, element0, element1));

            BlockIndex = blockIndex;
            GateEndIndex = (element1 >> 24) & 0xff;
            Tag = tag;
        }

        /// <summary>
        /// Return the number of the trailing suppressed samples in the last block of the gate.
        /// </summary>
        public Int64 GetSuppressedSampleCount(ProcessingParameters processingParams)
        { return (processingParams.ProcessingBlockSamples - GateEndIndex) - (IsRecordStop ? 1 : 0); }

        /// <summary>
        /// Return the index (in the record) of the sample where the gate stop condition is met. It is the very first sample which goes below the configured ZeroSuppress threshold-Hysteresis.
        /// </summary>
        public Int64 GetStopSampleIndex(ProcessingParameters processingParams)
        { return (Int64)(BlockIndex - 1) * processingParams.ProcessingBlockSamples - GetSuppressedSampleCount(processingParams); }

        /// <summary>
        /// Returns true when the gate is ended by a record-stop.
        /// This means that part of the gate is not included in the record (because located outside the record acquisition window)
        /// </summary>
        public bool IsRecordStop { get { return Tag == MarkerTag.RecordStop; } }

        public readonly Int64 BlockIndex;   // Gate stop position (in processing blocks)
        public readonly Int32 GateEndIndex; // The index of one-past last sample of the gate. if IsRecordStop() returns `true` it indicates the index of the last sample of the gate.
        public readonly MarkerTag Tag;      // Marker tag type.
    }

    /// <summary>
    /// Represent a marker of a gate. A gate is delimited by start and stop markers
    /// </summary>
    class GateMarker
    {
        readonly public GateStartMarker StartMarker;    // start-gate marker
        readonly public StopMarker StopMarker;          // stop-gate marker

        /// <summary>
        /// Builds a gate-marker
        /// </summary>
        public GateMarker(GateStartMarker startMarker, StopMarker stopMarker)
        {
            if (stopMarker.BlockIndex < startMarker.BlockIndex)
            {
                // this might be caused when the block index counter wraps around. In such case, "stop.BlockIndex" must be incremented by 2**32 (recordIndex counter is 32-bit)
                throw new ArgumentException(String.Format("Gate start block index {0} exceeds stop block index {1}.", startMarker.BlockIndex, stopMarker.BlockIndex));
            }

            StartMarker = startMarker;
            StopMarker = stopMarker;
        }


        /// <summary>
        /// Return the number of samples associated with the gate stored in memory (including padding overhead).
        /// </summary>
        public Int64 GetStoredSampleCount(ProcessingParameters processingParams, StopMarker recordStop)
        {
            Int64 gateBlocks = StopMarker.BlockIndex- StartMarker.BlockIndex;
            Int64 postGateRecordBlocks = recordStop.BlockIndex - StopMarker.BlockIndex;

            if (postGateRecordBlocks < 0)
                throw new ArgumentException(String.Format("Block index of record-stop {0} is smaller than block index of gate-stop {1}.", recordStop.BlockIndex, StopMarker.BlockIndex));

            Int64 gateSamples = gateBlocks * processingParams.ProcessingBlockSamples;

            Int64 postGateRecordSamples = postGateRecordBlocks * processingParams.ProcessingBlockSamples;
            //// additional pre and post-gate samples are not stored if they are acquired after the record-stop (end of the record).
            Int64 additionalSamples = Math.Min(postGateRecordSamples, processingParams.PreGateSamples + processingParams.PostGateSamples);
            return Tools.AlignUp(gateSamples + additionalSamples, processingParams.StorageBlockSamples);
        }
    }

    /// <summary>
    /// Represent a record descriptor with trigger marker and a list of gate markers.
    /// </summary>
    class RecordDescriptor
    {
        public TriggerMarker TriggerMarker; // trigger marker
        public List<GateMarker> GateList;   // list of gate markers
        public StopMarker RecordStopMarker; // record-stop marker

        /// <summary>
        /// Return the number of samples associated with the gate stored in memory (including padding).
        /// </summary>
        public Int64 GetStoredSampleCount(ProcessingParameters processingParams)
        {
            if(GateList == null)
                return 0;

            return GateList.Sum(item => item.GetStoredSampleCount(processingParams, RecordStopMarker));
        }
    }

    /// <summary>
    /// Represent a decoder of marker stream.
    /// </summary>
    /// The class is designed to process a certain number of records in perpetual streaming context. We might want to process
    /// more than one record in order to optimize data transfer time from instrument-to-host.
    ///
    /// Typical use of such class would be:
    ///
    ///         MarkerStreamDecoder decoder = new MarkerStreamDecoder();
    ///         for(;;) // infinite loop
    ///         {
    ///             // keep decoding the number of records
    ///             long offset = 0;
    ///             while(stream.size() > 0 && decoder.RecordList.Count < MinimumNumberOfRecords)
    ///                 decoder.DecodeNextMarker(stream, ref offset);
    ///
    ///             // process records
    ///             for(int i = 0; i<MinimumNumberOfRecords; ++i)
    ///             {
    ///                 RecordDescriptor recordDesc = decoder.RecordQueue.Deqeue();
    ///                 // do something with "recordDesc"
    ///             }
    ///         }
    class MarkerStreamDecoder
    {
        /// <summary>
        /// Decode the next marker from the given input stream.
        /// </summary>
        /// <param name="stream">Stream to decode markers from</param>
        /// <param name="offsetInStream"> Offset in "stream"</param>
        public void DecodeNextMarker(IAqMD3StreamElements<Int32> stream, ref long offsetInStream)
        {
            if (offsetInStream >= stream.ActualElements)
                throw new ArgumentException("Cannot decode markers from empty stream");

            MarkerTag tag = (MarkerTag)(stream[offsetInStream] & 0xff);

            bool isRecordStop = false;
            switch(tag)
            {
                case MarkerTag.TriggerNormal:
                case MarkerTag.TriggerAverager:
                    {
                        CurrentRecord = new RecordDescriptor();
                        CurrentRecord.TriggerMarker = DecodeTriggerMarker(stream, ref offsetInStream);
                        break;
                    }
                case MarkerTag.GateStart:
                    {
                        var gate = DecodeGateMarker(stream, ref offsetInStream);
                        if (CurrentRecord.GateList == null)
                            CurrentRecord.GateList = new List<GateMarker>();
                        CurrentRecord.GateList.Add(gate);
                        isRecordStop = gate.StopMarker.IsRecordStop;

                        if (gate.StopMarker.IsRecordStop)
                            CurrentRecord.RecordStopMarker = gate.StopMarker;
                        break;
                    }
                case MarkerTag.RecordStop:
                    {
                        var stop = DecodeStopMarker(stream, ref offsetInStream);
                        CurrentRecord.RecordStopMarker = stop;
                        isRecordStop = stop.IsRecordStop;
                        break;
                    }
                case MarkerTag.DummyGate:
                    WalkthroughDummyMarker(stream, ref offsetInStream);
                    break;
                default:
                    throw new ArgumentException(String.Format("Unexpected tag: {0}", tag));
            }

            if (isRecordStop)
            {
                if (RecordDescriptorQueue == null)
                    RecordDescriptorQueue = new Queue<RecordDescriptor>();

                RecordDescriptorQueue.Enqueue(CurrentRecord);
            }
        }

        /// <summary>
        /// Return the number of complete record descriptors in the queue.
        /// </summary>
        public int NumAvailableRecords { get { return RecordDescriptorQueue == null ? 0 : RecordDescriptorQueue.Count;  } }

        /// <summary>
        /// Expect a trigger marker from the input marker "stream" at "offset", decode it and return it as a result.
        /// </summary>
        private static TriggerMarker DecodeTriggerMarker(IAqMD3StreamElements<Int32> stream, ref long offset)
        {
            TriggerMarker result = new TriggerMarker(stream[offset], stream[offset + 1], stream[offset + 2]);
            offset += 16;
            return result;
        }

        /// <summary>
        /// Expect a gate start marker from the input marker "stream" at "offset", decode it and return it as a result.
        /// </summary>
        private static GateStartMarker DecodeGateStartMarker(IAqMD3StreamElements<Int32> stream, ref long offset)
        {
            GateStartMarker result = new GateStartMarker(stream[offset], stream[offset+1]);
            offset += 2;
            return result;
        }
        /// <summary>
        /// Expect a (gate or record) stop marker from the input marker "stream" at "offset", decode it and return it as a result.
        /// </summary>
        private static StopMarker DecodeStopMarker(IAqMD3StreamElements<Int32> stream, ref long offset)
        {
            StopMarker result = new StopMarker(stream[offset], stream[offset + 1]);
            offset += 2;
            return result;
        }

        /// <summary>
        /// Expect a gate marker (start+stop) from the input marker stream, decode it and return it as a result.
        /// </summary>
        private static GateMarker DecodeGateMarker(IAqMD3StreamElements<Int32> stream, ref long offset)
        {
            var start = DecodeGateStartMarker(stream, ref offset);
            var stop = DecodeStopMarker(stream, ref offset);
            return new GateMarker(start, stop);
        }
        /// <summary>
        /// Expect a dummy gate marker and consume it from the input marker "stream" by increasing "offset".
        /// </summary>
        private static void WalkthroughDummyMarker(IAqMD3StreamElements<Int32> stream, ref long offset)
        { offset += 2; }

        public Queue<RecordDescriptor> RecordDescriptorQueue; // The queue of well-formed record descriptors.
        private RecordDescriptor CurrentRecord;               // Current record descriptor (it might be incomplete).
    }
}
