using Acqiris.AqMD3;
using LibTools;
using System.Threading;
using System.IO;
using System;


namespace CS_StreamingZeroSuppress
{
    class App
    {
        static void Main(string[] args)
        {
            Console.WriteLine("  CS_StreamingZeroSuppress\n");

            // Edit resource and options as needed. Resource is ignored if option Simulate=true.
            string resourceDesc = "PXI40::0::0::INSTR";

            // An input signal is necessary if the example is run in non simulated mode, otherwise
            // the acquisition will time out. Set the Simulate option to false to disable simulation.
            string initOptions = "Simulate=true, DriverSetup= Model=SA248P";

            bool idQuery = true;
            bool reset = true;

            TimeSpan streamingDuration = TimeSpan.FromSeconds(10);

            try
            {
                // Initialize the driver. See driver help topic "Initializing the IVI.NET Driver" for additional information.
                using (var driver = new AqMD3(resourceDesc, idQuery, reset, initOptions))
                {
                    Console.WriteLine("Driver initialized");

                    // Abort execution if instrument is still in simulated mode.
                    if (driver.DriverOperation.Simulate)
                    {
                        Console.WriteLine("The Streaming & ZeroSuppress features are not supported in simulated mode.");
                        Console.WriteLine("Please update the resource string (strResourceDesc) to match your configuration, and update the init options string (strInitOptions) to disable simulation.");
                        Console.WriteLine("Interrupted - Press enter to exit");
                        Console.ReadLine();
                    }

                    // Check the instrument contains the required CST & ZS1 options.
                    if (!(driver.InstrumentInfo.Options.Contains("CST") && driver.InstrumentInfo.Options.Contains("ZS1")))
                    {
                        Console.WriteLine("The required CST & ZS1 module options are missing from the instrument.");
                        Console.WriteLine("Interrupted - Press enter to exit");
                        Console.ReadLine();
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

                    // Configure Acquisition
                    const long numPointsPerRecord = 1024;
                    const StreamingMode streamingMode = StreamingMode.Triggered;
                    const AcquisitionMode acqMode = AcquisitionMode.Normal;
                    const int numAverages = 8;
                    const DataReductionMode dataReductionMode = DataReductionMode.ZeroSuppress;
                    Console.WriteLine("Configuring acquisition");
                    Console.WriteLine("Record size:        {0}", numPointsPerRecord);
                    Console.WriteLine("Acqisition Mode:    {0}", acqMode);
                    Console.WriteLine("Streaming Mode:     {0}", streamingMode);
                    Console.WriteLine("Data Reduction Mode:{0}", dataReductionMode);
                    Console.WriteLine("Number of averages: {0}", numAverages);
                    driver.Acquisition.RecordSize = numPointsPerRecord;
                    driver.Acquisition.Mode = acqMode;
                    driver.Acquisition.NumberOfAverages = numAverages;
                    driver.Acquisition.Streaming.Mode = streamingMode;
                    driver.Acquisition.DataReductionMode = dataReductionMode;

                    // Configure channel.
                    const double channelRange = 2.5;
                    const double channelOffset = 0.0;
                    const VerticalCoupling channelCoupling = VerticalCoupling.DC;
                    Console.WriteLine("Configuring channel");
                    Console.WriteLine("Range:              {0}", channelRange);
                    Console.WriteLine("Offset:             {0}", channelOffset);
                    Console.WriteLine("Coupling:           {0}", channelCoupling);
                    driver.Channels["Channel1"].Configure(channelRange, channelOffset, channelCoupling, true);

                    // Configure ZeroSuppress
                    const int zsThreshold = 0;
                    const int zsHysteresis = 300;
                    const int zsPreGateSamples = 0;
                    const int zsPostGateSamples = 0;
                    Console.WriteLine("Configuring ZeroSuppress");
                    Console.WriteLine("Threshold:          {0}", zsThreshold);
                    Console.WriteLine("Hysteresis:         {0}", zsHysteresis);
                    Console.WriteLine("PreGate Samples:    {0}", zsPreGateSamples);
                    Console.WriteLine("PostGate Samples:   {0}", zsPostGateSamples);
                    driver.Channels["Channel1"].ZeroSuppress.Threshold= zsThreshold;
                    driver.Channels["Channel1"].ZeroSuppress.Hysteresis = zsHysteresis;
                    driver.Channels["Channel1"].ZeroSuppress.PreGateSamples = zsPreGateSamples;
                    driver.Channels["Channel1"].ZeroSuppress.PostGateSamples = zsPostGateSamples;

                    // Configure the trigger.
                    const string activeSource = "Internal1";
                    const double level = 0.0;
                    const TriggerSlope slope = TriggerSlope.Positive;
                    Console.WriteLine("Configuring trigger\n");
                    Console.WriteLine("Active Source:               {0}", activeSource);
                    Console.WriteLine("Level:     {0}", level);
                    Console.WriteLine("Slope:     {0}", slope);
                    driver.Trigger.ActiveSource = activeSource;
                    driver.Trigger.Sources[activeSource].Level = level;
                    driver.Trigger.Sources[activeSource].Edge.Slope = slope;

                    // Calibrate the instrument.
                    Console.WriteLine("Performing self-calibration\n");
                    driver.Calibration.SelfCalibrate();

                    IAqMD3Stream sampleStream = driver.Streams["StreamCh1"];
                    IAqMD3Stream markerStream = driver.Streams["MarkersCh1"];

                    const long nbrMarkerElementsToFetch = 1024;
                    Int64 numSamplesPerElement = (acqMode == AcquisitionMode.Averager) ? 1 : 2;
                    Int64 numElements = numPointsPerRecord / numSamplesPerElement;

                    Int64 sampleBufferSize = numElements /*useful buffer size*/ + numElements / 2 /* unfolding overhead (only required for SA248) */;

                    IAqMD3StreamElements<int> markerBuffer = driver.Acquisition.CreateStreamElementsInt32(nbrMarkerElementsToFetch);
                    IAqMD3StreamElements<int> sampleBuffer = driver.Acquisition.CreateStreamElementsInt32(sampleBufferSize);

                    ProcessingParameters processingParams = GetProcessingParametersForModel(driver.Identity.InstrumentModel, acqMode, zsPreGateSamples, zsPostGateSamples);
                    double sampleInterval = 1.0 / driver.Acquisition.SampleRate;


                    MarkerStreamDecoder streamDecoder = new MarkerStreamDecoder();
                    long expectedRecordIndex = 0;

                    long totalMarkerElements = 0;
                    long totalSampleElements = 0;

                    // Initiate the acquisition.
                    Console.WriteLine("Initiating acquisition\n");
                    driver.Acquisition.Initiate();

                    using (var outputFile = new StreamWriter("StreamingZeroSuppress.log"))
                    {
                        outputFile.WriteLine("model             : {0}", driver.Identity.InstrumentModel);
                        outputFile.WriteLine("record size       : {0}", numPointsPerRecord);
                        outputFile.WriteLine("threshold         : {0}", zsThreshold);
                        outputFile.WriteLine("hysteresis        : {0}", zsHysteresis);
                        outputFile.WriteLine("pre-gate samples  : {0}", zsPreGateSamples);
                        outputFile.WriteLine("post-gate samples : {0}", zsPostGateSamples);
                        outputFile.WriteLine();

                        var endTime = DateTime.Now + streamingDuration;

                        while (DateTime.Now <= endTime)
                        {
                            // Wait for markers
                            while((DateTime.Now <= endTime) && ((streamDecoder.NumAvailableRecords == 0)))
                            {
                                FetchAvailableElements(markerStream, nbrMarkerElementsToFetch, ref markerBuffer);

                                // If the fetch fails to read data, then wait before a new attempt.
                                if ((markerBuffer == null) || (markerBuffer.ActualElements == 0))
                                {
                                    Console.WriteLine("Waiting for data");
                                    const int dataWaitTimeMs = 200; // in milliseconds
                                    Thread.Sleep(dataWaitTimeMs);
                                    continue;
                                }

                                totalMarkerElements += markerBuffer.ActualElements;

                                Console.WriteLine("Fetched {0} marker elements from {1}. Remaining elements: {2}", markerBuffer.ActualElements, markerStream.Name, markerBuffer.AvailableElements);

                                // decode all fetched marker elements
                                for (long offsetInBuffer = 0; offsetInBuffer < markerBuffer.ActualElements;)
                                {
                                    /*offsetInBuffer is incremented by "DecodeNextMarker"*/
                                    streamDecoder.DecodeNextMarker(markerBuffer, ref offsetInBuffer);
                                }
                            }

                            // Check the count of available complete records.
                            if (streamDecoder.NumAvailableRecords == 0)
                                continue;

                            // Get the descriptor of next record from the decoder and check its consistency.
                            var recordDescriptor = streamDecoder.RecordDescriptorQueue.Dequeue();

                            // Check that the record descriptor holds the expected record index and the expected tag
                            if ((expectedRecordIndex & TriggerMarker.RecordIndexMask) != recordDescriptor.TriggerMarker.RecordIndex)
                                throw new Exception(String.Format("Unexpected record index: expected={0}, got {1}", expectedRecordIndex, recordDescriptor.TriggerMarker.RecordIndex));

                            ++expectedRecordIndex;

                            // Compute the number of associated elements stored in memory
                            long nbrPackedRecordElements = recordDescriptor.GetStoredSampleCount(processingParams) / numSamplesPerElement;
                            if (nbrPackedRecordElements == 0)
                            {
                                // All record-samples are suppressed => no need to fetch data from sample stream
                                UnpackRecord(recordDescriptor, acqMode, numPointsPerRecord, null, processingParams, sampleInterval, outputFile);
                            }
                            else
                            {
                                // Fetch samples
                                sampleBuffer = sampleStream.FetchDataInt32(nbrPackedRecordElements, sampleBuffer);
                                totalSampleElements += sampleBuffer.ActualElements;

                                if (sampleBuffer.ActualElements != nbrPackedRecordElements)
                                    throw new Exception(String.Format("Total fetched elements {0} for packed record is different than requested number of elements {1}", sampleBuffer.ActualElements, nbrPackedRecordElements));

                                Console.WriteLine("Fetched {0} data elements from {1}. Remaining elements: {2}", sampleBuffer.ActualElements, sampleStream.Name, sampleBuffer.AvailableElements);

                                // Rebuild the record and print related information into output file
                                UnpackRecord(recordDescriptor, acqMode, numPointsPerRecord, sampleBuffer, processingParams, sampleInterval, outputFile);
                            }

                        }
                    }

                    Int64 totalSampleData = totalSampleElements * sizeof(Int32);
                    Int64 totalMarkerData = totalMarkerElements * sizeof(Int32);
                    Console.WriteLine();
                    Console.WriteLine("Total sample data read: {0} MBytes.", totalSampleData / (1024 * 1024));
                    Console.WriteLine("Total marker data read: {0} MBytes.", totalMarkerData / (1024 * 1024));
                    Console.WriteLine("Duration: {0} seconds.", streamingDuration);
                    Int64 totalData = totalSampleData + totalMarkerData;
                    Console.WriteLine("Data rate: {0}  MB/s.", (totalData) / (1024 * 1024) / (streamingDuration.TotalSeconds));

                    // Stop the acquisition.
                    Console.WriteLine("Stopping acquisition");
                    driver.Acquisition.Abort();

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
            Console.Read();
        }

        /// <summary>
        /// Fetch "nbrElementsToFetch" elements from "stream" if the number of elements available on the module exceeds the requested number. Otherwise, fetch all available elements.
        /// </summary>
        /// <param name="stream "> The instance of the stream to fetch from.</param>
        /// <param name="nbrElementsToFetch"> The desired number of elements to fetch</param>
        /// <param name="fetchBuffer"> The buffer of stream elements to use for the fetch</param>
        private static void FetchAvailableElements(IAqMD3Stream stream, long nbrElementsToFetch, ref IAqMD3StreamElements<int> fetchBuffer)
        {
            fetchBuffer = stream.FetchDataInt32(nbrElementsToFetch, fetchBuffer);

            if (fetchBuffer.ActualElements == 0 && fetchBuffer.AvailableElements != 0)
                fetchBuffer = stream.FetchDataInt32(fetchBuffer.AvailableElements, fetchBuffer);
        }


        /// <summary>
        /// Unpack the gates of a record described by descriptor 'recordDesc' into 'output'.
        /// </summary>
        /// <param name="record">Record descriptor</param>
        /// <param name="recordSize">Applied record size</param>
        /// <param name="sampleBuffer">Buffer of sample stream elements representing the packed gates of the record</param>
        /// <param name="processingParams">Processing parameters</param>
        /// <param name="outputFile">The output file to write record information into.</param>
        private static void UnpackRecord(RecordDescriptor record, AcquisitionMode acqMode,Int64 recordSize, IAqMD3StreamElements<int> sampleBuffer, ProcessingParameters processingParams, double sampleInterval, StreamWriter outputFile)
        {
            double triggerTime = record.TriggerMarker.GetTriggerTime(processingParams.TimestampingPeriod);
            double startTime = record.TriggerMarker.GetStartTime(sampleInterval);

            outputFile.WriteLine("# record index       : {0}", record.TriggerMarker.RecordIndex);
            outputFile.WriteLine(" * Time of Sample #0 : {0}", triggerTime + startTime);
            outputFile.WriteLine(" * Time of Trigger   : {0}", triggerTime);


            // track the actual size of record (as invalid pre-record samples might be stored by the firmware)
            Int64 actualRecordSize = recordSize;

            // The offset at which starts storage of gate samples (including the leading suppressed samples)
            Int64 nextGateOffsetInMemory = 0;

            if(record.GateList != null)
            {
                /* A gate descriptor describes data samples stored in memory in the following way:
                                                      samples as they are stored in memory
                            +---------+-----------------+--------------------------------+----------------+---------+
                            | padding |  pre gate       |          gate                  |      post gate | padding |
                            +---------+-----------------+--------------------------------+----------------+---------+
                                                        ^                                 ^
                                                        |                                 |
                                                        +-------+       +-----------------+
                                                                |       |
                                                          +-----+---+---+----+
                                                          |  start  |  stop  |
                                                          +---------+--------+
                                                             gate-descriptor

                     Where:
                     - The leading padding corresponds to the samples before the start-gate sample(in the same processing block). When pre-gate is different than zero, these samples correspond to samples before pre-gate.
                     - The "start" index(returned by #GateStartMarker::GetStartSampleIndex) indicates the first sample of the gate (the sample which exceeds the configured threshold).
                     - The "stop" index (returned by #GateStopMarker::GetStopSampleIndex) indicates the one-past last sample of the gate. It is the first sample falling below threshold-hysteresis.
                     - The trailing padding corresponds to: 1) samples located after the gate-stop(or post-gate) in the same processing block, and/or 2) extra padding added to fit DDR alignment constraint(512-bit).*/
                foreach (var gate in record.GateList)
                {
                    Int64 gateStartIndex = gate.StartMarker.GetStartSampleIndex(processingParams);
                    Int64 gateStopIndex = gate.StopMarker.GetStopSampleIndex(processingParams);

                    Int64 leadingSamplesToSkip = gate.StartMarker.SuppressedSampleCount;
                    if (gateStartIndex < processingParams.PreGateSamples)
                    {
                        // pre-gate samples acquired before the very first sample of the record are not correct. They must be removed from the record.
                        Int64 preRecordSamples = processingParams.PreGateSamples - gateStartIndex;
                        // samples are stored by blocks of #processingBlockSamples samples.
                        Int64 blockSize = processingParams.ProcessingBlockSamples;
                        Int64 invalidStoredSamples = ((preRecordSamples % blockSize) == 0) ? preRecordSamples : ((preRecordSamples / blockSize) + 1) * blockSize;
                        actualRecordSize = Math.Max(0, recordSize - invalidStoredSamples);
                        leadingSamplesToSkip = invalidStoredSamples;
                    }

                    Int64 dataStartIndex = Math.Max((Int64)0, gateStartIndex - processingParams.PreGateSamples);
                    Int64 dataStopIndex = Math.Min(gateStopIndex + processingParams.PostGateSamples, actualRecordSize);
                    Int64 dataStartIndexInMemory  = nextGateOffsetInMemory + leadingSamplesToSkip;

                    outputFile.Write(" - Gate samples= #{0}..{1}, pre-gate=#{2}, post-gate=#{3}, data samples(#{4}..{5})=[",
                        gateStartIndex,                     // is the index of the first sample of the gate (above the configured threshold)
                        gateStopIndex -1,                   // is the index of the last sample of the gate (above the configured threshold-hysteresis)
                        gateStartIndex - dataStartIndex,    // actual pre-gate samples
                        dataStopIndex - gateStopIndex,      // actual post-gate samples (might be negative)
                        dataStartIndex,                     // is the index of the first valid pre-gate sample
                        dataStopIndex                       // is the index of the last valid sample of the gate or post-gate.
                        );

                    for (Int64 sampleIndexInMemory = dataStartIndexInMemory, samplePosition = dataStartIndex; samplePosition < dataStopIndex; ++samplePosition, ++sampleIndexInMemory)
                    {
                        if(acqMode == AcquisitionMode.Averager)
                            outputFile.Write("{0}", sampleBuffer[sampleIndexInMemory]);
                        else
                            outputFile.Write("{0}", GetSample(sampleBuffer, sampleIndexInMemory));

                        if (samplePosition < dataStopIndex - 1)
                            outputFile.Write(", ");
                    }

                    outputFile.WriteLine("]");
                    nextGateOffsetInMemory += gate.GetStoredSampleCount(processingParams, record.RecordStopMarker);
                }
            }

            outputFile.WriteLine("actual record size: {0}", actualRecordSize);
            outputFile.WriteLine();
        }

        /// <summary>
        /// Get the sample at 'index' as 16-bit signed integer from the stream of 32-bit elements.
        /// </summary>
        /// <param name="sampleBuffer">Buffer of 32-bit elements encoding 16-bit samples.</param>
        /// <param name="index">The index of the 16-bit sample to return</param>
        /// <returns>16-bit sample located at "index"</returns>
        private static Int16 GetSample(IAqMD3StreamElements<Int32> sampleBuffer, Int64 index)
        {
            Int64 elementIndex = index / 2;
            Int32 element = sampleBuffer[elementIndex];

            if((index % 2) == 0)
                return (Int16)(element & 0xffff);
            else
                return (Int16)((element >> 16) & 0xffff);
        }

        /// <summary>
        /// Return the processing parameters for the given instrument model.
        /// </summary>
        private static ProcessingParameters GetProcessingParametersForModel(string model, AcquisitionMode acquisitionMode, int preGateSamples, int postGateSamples)
        {
            if(acquisitionMode == Acqiris.AqMD3.AcquisitionMode.Averager)
            {
                if (model == "SA220P" || model == "SA220E")
                    return new ProcessingParameters(16, 16, 500e-12, preGateSamples, postGateSamples);
                throw new ArgumentOutOfRangeException("Model", model, "Real-Time Binning not supported by your instrument model");
            }
            else
            {
                if (model == "SA220P" || model == "SA220E")
                    return new ProcessingParameters(32, 8, 500e-12, preGateSamples, postGateSamples);
                else if (model == "SA230P" || model == "SA230E")
                    return new ProcessingParameters(32, 16, 250e-12, preGateSamples, postGateSamples);
                else if (model == "SA240P" || model == "SA240E")
                    return new ProcessingParameters(32, 16, 250e-12, preGateSamples, postGateSamples);
                else if (model == "SA248P" || model == "SA248E")
                    return new ProcessingParameters(64, 32, 125e-12, preGateSamples, postGateSamples);
                else
                    throw new ArgumentOutOfRangeException("Model", model, "Cannot deduce the size of processing block for your instrument model");

            }
        }

}
}
