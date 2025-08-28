using System;
using System.IO;
using System.Threading;
using Acqiris.AqMD3;

namespace CS_Streaming
{
    class App
    {
        static void Main(string[] args)
        {
            Console.WriteLine("  CS_Streaming");
            Console.WriteLine();

            // Edit resource and options as needed. Resource is ignored if option Simulate=true.
            string resourceDesc = "PXI40::0::0::INSTR";

            // An input signal is necessary if the example is run in non simulated mode, otherwise
            // the acquisition will time out. Set the Simulate option to false to disable simulation.
            string initOptions = "Simulate=true, DriverSetup= Model=SA220P";

            bool idquery = false;
            bool reset = false;

            TimeSpan streamingDuration = TimeSpan.FromMinutes(10);

            try
            {
                // Initialize the driver. See driver help topic "Initializing the IVI.NET Driver" for additional information.
                using (var driver = new AqMD3(resourceDesc, idquery, reset, initOptions))
                {
                    Console.WriteLine("Driver initialized");

                    // Abort execution if instrument is still in simulated mode.
                    if (driver.DriverOperation.Simulate)
                    {
                        Console.WriteLine("The Averager and Streaming features are not supported in simulated mode.");
                        Console.WriteLine("Please update the resource string (resourceDesc) to match your configuration, and update the init options string (initOptions) to disable simulation.");
                        Console.WriteLine("Interrupted - Press enter to exit");
                        Console.Read();
                        System.Environment.Exit(-1);
                    }

                    // Check the instrument contains the required CST module option.
                    if (!driver.InstrumentInfo.Options.Contains("CST"))
                    {
                        Console.WriteLine("The required CST module option is missing from the instrument.");
                        Console.WriteLine("Interrupted - Press enter to exit");
                        Console.Read();
                        System.Environment.Exit(-1);
                    }

                    // Print a few IIviDriverIdentity properties.
                    Console.WriteLine("Driver identifier:  {0}", driver.Identity.Identifier);
                    Console.WriteLine("Driver revision:    {0}", driver.Identity.Revision);
                    Console.WriteLine("Driver vendor:      {0}", driver.Identity.Vendor);
                    Console.WriteLine("Driver description: {0}", driver.Identity.Description);
                    Console.WriteLine("Instrument model:   {0}", driver.Identity.InstrumentModel);
                    Console.WriteLine("Firmware revision:  {0}", driver.Identity.InstrumentFirmwareRevision);
                    Console.WriteLine("Serial number:      {0}", driver.InstrumentInfo.SerialNumberString);
                    Console.WriteLine("Options:            {0}", driver.InstrumentInfo.Options);
                    Console.WriteLine("Simulate:           {0}", driver.DriverOperation.Simulate);
                    Console.WriteLine("");


                    // Configure the acquisition
                    const long numPointsPerRecord = 1024*16;
                    const int numAverages = 80;
                    const AcquisitionMode acquisitionMode = AcquisitionMode.Normal;
                    const StreamingMode streamingMode = StreamingMode.Triggered;
                    const long numRecordsToFetchAtOnce = 128;
                    Console.WriteLine("Configuring Acquisition");
                    Console.WriteLine("Number of Averages:                  {0}", numAverages);
                    Console.WriteLine("Number of records to fetch at Once:  {0}", numRecordsToFetchAtOnce);
                    Console.WriteLine("Record size:                         {0}", numPointsPerRecord);
                    Console.WriteLine("Acquisition Mode:                    {0}", acquisitionMode);
                    Console.WriteLine("Streaming Mode:                      {0}", streamingMode);
                    driver.Acquisition.RecordSize = numPointsPerRecord;
                    driver.Acquisition.NumberOfAverages = numAverages;
                    driver.Acquisition.Mode = acquisitionMode;
                    driver.Acquisition.Streaming.Mode = streamingMode;

                    // Configure the channel
                    const double channelRange = 1.0;
                    const double channelOffset = 0.0;
                    const VerticalCoupling channelCoupling = VerticalCoupling.DC;
                    const bool timeInterleavingEnabled = false;
                    Console.WriteLine("Configuring Channel");
                    Console.WriteLine("Range:              {0}", channelRange);
                    Console.WriteLine("Offset:             {0}", channelOffset);
                    Console.WriteLine("Coupling:           {0}", channelCoupling);
                    Console.WriteLine("Time Interleaving:  {0}", timeInterleavingEnabled ? "Enabled" : "Disabled");
                    driver.Channels["Channel1"].Configure(channelRange, channelOffset, channelCoupling, true);
                    driver.Channels["Channel1"].TimeInterleavedChannelList = timeInterleavingEnabled ? "Channel2" : "";
                    if (driver.Identity.InstrumentModel.StartsWith("SA120"))
                    {
                        // Disable unused channel
                        driver.Channels["Channel2"].Enabled = false;
                    }

                    // Configure the trigger.
                    const string activeSource = "Internal1";
                    const double triggerLevel = 0.0;
                    const TriggerSlope triggerSlope = TriggerSlope.Positive;
                    Console.WriteLine("Configuring trigger");
                    Console.WriteLine("Active Source:      {0}", activeSource);
                    Console.WriteLine("Level:              {0}", triggerLevel);
                    Console.WriteLine("Slope:              {0}\n", triggerSlope);
                    driver.Trigger.ActiveSource = activeSource;
                    driver.Trigger.Sources[activeSource].Level = triggerLevel;
                    driver.Trigger.Sources[activeSource].Edge.Slope = triggerSlope;

                    // Calibrate the instrument.
                    Console.WriteLine("Performing self-calibration");
                    driver.Calibration.SelfCalibrate();

                    // prepare readout buffers
                    IAqMD3Stream sampleStream = driver.Streams["StreamCh1"];
                    IAqMD3Stream markerStream = driver.Streams["MarkersCh1"];

                    if ((acquisitionMode != AcquisitionMode.Averager) && (acquisitionMode != AcquisitionMode.Normal))
                        throw new ArgumentException(String.Format("Unexpected acquisition mode {0}", acquisitionMode));

                    int nbrSampleBytes = (driver.InstrumentInfo.NbrADCBits + 7) / 8;
                    long numSamplesPerElement = (acquisitionMode == AcquisitionMode.Averager) ? 1 : (4 / nbrSampleBytes);

                    long numRecordElements = numPointsPerRecord / numSamplesPerElement;
                    long numAcquisitionElements = numRecordElements * numRecordsToFetchAtOnce;
                    long numMarkerElementsToFetch = numRecordsToFetchAtOnce * TriggerMarker.RawElementCount;

                    long numSampleBufferElements = numAcquisitionElements         // required elements in buffer
                                                 + numAcquisitionElements / 2;    // unfolding overhead (only in single channel mode)

                    IAqMD3StreamElements<Int32> sampleBuffer = driver.Acquisition.CreateStreamElementsInt32(numSampleBufferElements);
                    IAqMD3StreamElements<Int32> markerBuffer = driver.Acquisition.CreateStreamElementsInt32(numMarkerElementsToFetch);

                    // start the acquisition.
                    driver.Acquisition.Initiate();

                    using (var outputFile = new StreamWriter("Streaming.log"))
                    {
                        long expectedRecordIndex = 0;
                        long remainingMarkerElements = 0;
                        long totalMarkerElements = 0;
                        long totalSampleElements = 0;
                        double timestampingPeriod = GetTimestampingPeriodForModel(driver.Identity.InstrumentModel, timeInterleavingEnabled);
                        double samplePeriod = 1.0 / driver.Acquisition.SampleRate;
                        int recordDurationInMs = Math.Max((int)(driver.Acquisition.RecordSize * samplePeriod * 1000.0), 1);
                        int numWaitForSamplesAttempts = (acquisitionMode == AcquisitionMode.Averager) ? driver.Acquisition.NumberOfAverages * 3 : 3;

                        const TriggerMarker.MarkerTag expectedTag = (acquisitionMode == AcquisitionMode.Averager) ? TriggerMarker.MarkerTag.TriggerAverager : TriggerMarker.MarkerTag.TriggerNormal;

                        var endTime = DateTime.Now + streamingDuration;

                        while (DateTime.Now <= endTime)
                        {
                            // only wait for data if the number of elements on the module is less than the desired data-volume for fetch.
                            if (remainingMarkerElements < numMarkerElementsToFetch)
                            {
                                Console.WriteLine("wait for data\n");
                                const int dataWaitTime = 200;
                                Thread.Sleep(dataWaitTime);
                            }

                            // fetch markers
                            markerBuffer = markerStream.FetchDataInt32(numMarkerElementsToFetch, markerBuffer);
                            if (markerBuffer.ActualElements == 0)
                                continue; // data is not ready, loop and wait.

                            totalMarkerElements += markerBuffer.ActualElements;
                            remainingMarkerElements = markerBuffer.AvailableElements;
                            Console.WriteLine("Fetched {0} marker elements from {1}. Remaining elements: {2}", markerBuffer.ActualElements, markerStream.Name, remainingMarkerElements);

                            // fetch samples
                            for (int attempts = 0; ; ++attempts)
                            {
                                sampleBuffer = sampleStream.FetchDataInt32(numAcquisitionElements, sampleBuffer);

                                if (sampleBuffer.ActualElements == numAcquisitionElements)
                                {
                                    totalSampleElements += sampleBuffer.ActualElements;
                                    Console.WriteLine("Fetched {0} sample elements from {1}. Remaining elements: {2}", sampleBuffer.ActualElements, sampleStream.Name, sampleBuffer.AvailableElements);
                                    break;
                                }

                                if(numWaitForSamplesAttempts <= attempts)
                                    throw new Exception(String.Format("Failed to fetch requested data from {0} after {1} attempts.", sampleStream.Name, numWaitForSamplesAttempts));

                                if (sampleBuffer.ActualElements == 0 && sampleBuffer.AvailableElements < numAcquisitionElements)
                                {
                                    /* Sometimes, the fetch fail because data might not be ready for fetch immediatly.
                                       Make another attempt after a short wait. */
                                    Console.WriteLine("Wait for record samples to be ready for fetch\n");
                                    Thread.Sleep(recordDurationInMs);
                                    continue;
                                }

                                /* The following error might occurs in case of stream overflow error where sample storage in memory is interrupted
                                   at overflow event. The very last record is incomplete in this case.*/
                                throw new Exception(String.Format("Fetch of sample elements is incomplete. Expected {0} elements, got {1} elements", numAcquisitionElements, sampleBuffer.ActualElements));
                            }


                            // process data associated with records
                            for (long markerOffset = 0; markerOffset < markerBuffer.ActualElements; markerOffset += TriggerMarker.RawElementCount)
                            {
                                // decode the next trigger marker
                                TriggerMarker triggerMarker = TriggerMarker.DecodeTriggerMarker(markerBuffer, markerOffset);

                                // Check that the marker holds the expected record index and the expected tag
                                if(triggerMarker.Tag != expectedTag)
                                    throw new Exception(String.Format("Unexpected tag: expected={0}, got {1}", expectedTag, triggerMarker.Tag));

                                if ((expectedRecordIndex & TriggerMarker.RecordIndexMask) != triggerMarker.RecordIndex)
                                    throw new Exception(String.Format("Unexpected record index: expected={0}, got {1}", expectedRecordIndex, triggerMarker.RecordIndex));

                                ++expectedRecordIndex;

                                // save trigger information and samples into output file
                                long recordIndex = markerOffset / TriggerMarker.RawElementCount;
                                long firstRecordElementOffset = recordIndex * numRecordElements;

                                SaveRecord(triggerMarker, numRecordElements, sampleBuffer, firstRecordElementOffset, timestampingPeriod, samplePeriod, outputFile);
                            }
                        }
                    }

                    // stop the acquisition.
                    driver.Acquisition.Abort();
                    Console.WriteLine("Acquisition completed");

                    // Close the driver.
                    driver.Close();
                    Console.WriteLine("Driver closed");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            Console.Write("\nDone - Press enter to exit");
            Console.ReadLine();
        }

        static double GetTimestampingPeriodForModel(string model, bool timeInterleavingEnabled=false)
        {
            if (model.StartsWith("SA220"))
                return 500e-12;
            else if (model.StartsWith("SA230"))
                return 250e-12;
            else if (model.StartsWith("SA240"))
                return 250e-12;
            else if (model.StartsWith("SA217"))
                return 250e-12;
            else if (model.StartsWith("SA248"))
                return 125e-12;
            else if (model.StartsWith("SA108"))
                return 1e-9;
            else if (model.StartsWith("SA120"))
                return timeInterleavingEnabled ? 500e-12 : 1e-9;
            else
                throw new ArgumentException("Cannot determine timestamping period for model: " + model);
        }

        /// <summary>
        /// Save record information in output steam.
        /// </summary>
        /// <param name="triggerMarker">Descriptor of trigger marker.</param>
        /// <param name="numRecordElements">Number of record elements.</param>
        /// <param name="sampleBuffer">Buffer of sample stream elements representing the packed gates of the record.</param>
        /// <param name="firstElementOffset">Offset of record elements in "sampleBuffer"</param>
        /// <param name="timestampingPeriod">Timestamping period</param>
        /// <param name="outputFile">The output file to write record information into.</param>
        static void SaveRecord(TriggerMarker triggerMarker, Int64 numRecordElements, IAqMD3StreamElements<Int32> sampleBuffer, Int64 firstElementOffset, double timestampingPeriod, double samplePeriod, StreamWriter outputFile)
        {
            double triggerTime = triggerMarker.GetTriggerTime(timestampingPeriod);
            double startTime = triggerMarker.GetStartTime(samplePeriod);

            outputFile.WriteLine("# record index                 : {0}", triggerMarker.RecordIndex);
            outputFile.WriteLine("# Absolute Time of First Sample: {0}", triggerTime + startTime);
            outputFile.WriteLine("# Absolute Time of Trigger     : {0}", triggerTime);

            outputFile.Write("Elements({0}) = [ ", numRecordElements);

            // Print all elements of small records.
            if (numRecordElements <= 10)
            {
                for (int i = 0; i<numRecordElements; ++i)
                    outputFile.Write("{0:x08} ", sampleBuffer[ firstElementOffset + i ]);
            }
            else
            {
                // print first 2 elements and last element
                outputFile.Write("{0:x08} {1:x08} ... {2:x08}"
                       , sampleBuffer [firstElementOffset + 0]
                       , sampleBuffer [firstElementOffset + 1]
                       , sampleBuffer [firstElementOffset + numRecordElements - 1]);
            }

            outputFile.WriteLine("]\n");
        }

        /// <summary>
        /// Represents a trigger marker
        /// </summary>
        class TriggerMarker
        {
            public enum MarkerTag
            {
                None = 0x00,
                TriggerNormal = 0x01, // 512-bit: Trigger marker standard Normal acquisition mode.
                TriggerAverager = 0x02, // 512-bit: Trigger marker standard Averager acquisition mode.
            };

            /// <summary>
            /// Constructs a trigger marker object from a raw-marker. Raw marker is 512-bit lage (i.e 16 32-bit elements), Only the three leading elements are used.
            /// </summary>
            /// <param name="element0">First 32-bit element of the trigger marker packet.</param>
            /// <param name="element1">Second 32-bit element of the trigger marker packet.</param>
            /// <param name="element2">Third 32-bit element of the trigger marker packet.</param>
            public TriggerMarker(Int32 element0, Int32 element1, Int32 element2)
            {
                MarkerTag tag = (MarkerTag)(element0 & 0xff);

                if (tag != MarkerTag.TriggerNormal && tag != MarkerTag.TriggerAverager)
                    throw new ArgumentException(String.Format("Expected trigger marker, got {0}", tag));

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

            /// <summary>
            /// Expect a trigger marker from the input marker "stream" at "offset", decode it and return it as a result.
            /// </summary>
            public static TriggerMarker DecodeTriggerMarker(IAqMD3StreamElements<Int32> stream, long offset)
            {
                if ((stream.ActualElements - offset) < RawElementCount)
                    throw new ArgumentException(String.Format("Cannot decode trigger marker. Only {0} elements remain on buffer, minimum required={1}", (stream.ActualElements - offset), RawElementCount));

                TriggerMarker result = new TriggerMarker(stream[offset], stream[offset + 1], stream[offset + 2]);
                return result;
            }

            public const Int32 RecordIndexMask = 0x00ffffff;
            public const Int32 RawElementCount = 16;            // raw marker is 512-bit, so 16 32-bit elements.

            public readonly MarkerTag Tag;                      // the tag of trigger marker.
            public readonly UInt64 AbsoluteTriggerSampleIndex;  // The sample index where the trigger event occured.
            public readonly double TriggerSubSample;            // the subsample part of the trigger event.
            public readonly UInt32 RecordIndex;                 // index of the record
        }
    }
}
