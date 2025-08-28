using Acqiris.AqMD3;
using System.Threading;
using System.IO;
using System;

namespace CS_StreamingPeakList
{
    class App
    {
        static void Main(string[] args)
        {
            Console.WriteLine("  CS_StreamingPeakList\n");

            // Edit resource and options as needed. Resource is ignored if option Simulate=true.
            string resourceDesc = "PXI3::0::0::INSTR";

            // An input signal is necessary if the example is run in non simulated mode, otherwise
            // the acquisition will time out. Set the Simulate option to false to disable simulation.
            string initOptions = "Simulate=false, DriverSetup= Model=SA248P";

            bool idQuery = true;
            bool reset = true;

            try
            {
                // Initialize the driver. See driver help topic "Initializing the IVI.NET Driver" for additional information.
                using (var driver = new AqMD3(resourceDesc, idQuery, reset, initOptions))
                {
                    Console.WriteLine("Driver initialized");

                    // Abort execution if instrument is still in simulated mode.
                    if (driver.DriverOperation.Simulate)
                    {
                        Console.WriteLine("The Streaming & PeakList features are not supported in simulated mode.");
                        Console.WriteLine("Please update the resource string (strResourceDesc) to match your configuration, and update the init options string (strInitOptions) to disable simulation.");
                        Console.WriteLine("Interrupted - Press enter to exit");
                        Console.ReadLine();
                    }

                    // Check the instrument contains the required CST & PKL options.
                    if (!(driver.InstrumentInfo.Options.Contains("CST") && driver.InstrumentInfo.Options.Contains("PKL")))
                    {
                        Console.WriteLine("The required CST & PKL module options are missing from the instrument.");
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
                    const long numPointsPerRecord = 16384;
                    const StreamingMode streamingMode = StreamingMode.TriggeredCount;
                    const AcquisitionMode acqMode = AcquisitionMode.PeakList;
                    const long numRecords = 100;
                    Console.WriteLine("Configuring acquisition");
                    Console.WriteLine("    number of records:  {0}", numRecords);
                    Console.WriteLine("    Record size:        {0}", numPointsPerRecord);
                    Console.WriteLine("    Acquisition Mode:   {0}", acqMode);
                    Console.WriteLine("    Streaming Mode:     {0}", streamingMode);
                    driver.Acquisition.Mode = acqMode;
                    driver.Acquisition.Streaming.Mode = streamingMode;
                    driver.Acquisition.RecordSize = numPointsPerRecord;
                    driver.Acquisition.NumberOfRecordsToAcquire = numRecords;

                    // Configure channel.
                    const double channelRange = 2.5;
                    const double channelOffset = 0.0;
                    const VerticalCoupling coupling = VerticalCoupling.DC;
                    Console.WriteLine("Configuring channel");
                    Console.WriteLine("    Range:              {0}", channelRange);
                    Console.WriteLine("    Offset:             {0}", channelOffset);
                    Console.WriteLine("    Coupling:           {0}", coupling);
                    driver.Channels["Channel1"].Configure(channelRange, channelOffset, coupling, true);

                    // Configure Baseline Correction
                    const BaselineCorrectionMode blMode = BaselineCorrectionMode.Disabled;
                    const int blDigitalOffset = 0;
                    const int blPulseThreshold = 0;
                    const BaselineCorrectionPulsePolarity blPulsePolarity = BaselineCorrectionPulsePolarity.Positive;
                    Console.WriteLine("Configuring Baseline Correction");
                    Console.WriteLine("    Mode:               {0}", blMode);
                    Console.WriteLine("    Digital Offset:     {0}", blDigitalOffset);
                    Console.WriteLine("    Pulse Threshold:    {0}", blPulseThreshold);
                    Console.WriteLine("    Pulse Polarity:     {0}", blPulsePolarity);
                    driver.Channels["Channel1"].BaselineCorrection.Mode = blMode;
                    driver.Channels["Channel1"].BaselineCorrection.DigitalOffset = blDigitalOffset;
                    driver.Channels["Channel1"].BaselineCorrection.PulseThreshold = blPulseThreshold;
                    driver.Channels["Channel1"].BaselineCorrection.PulsePolarity = blPulsePolarity;

                    // Configure PeakList
                    const int pklValueSmoothingLength = 3;
                    const int pklDerivativeSmoothingLength = 7;
                    const int pklPulseValueThreshold = 512;
                    const int pklPulseDerivativeThresholdRising = 256;
                    const int pklPulseDerivativeThresholdFalling = -256;
                    const int pklPulseDerivativeHysteresis = 16;
                    const int pklBaseline = 0;
                    Console.WriteLine("Configuring PeakList");
                    Console.WriteLine("    Value Smoothing Length:            {0}", pklValueSmoothingLength);
                    Console.WriteLine("    Derivative Smoothing Length:       {0}", pklDerivativeSmoothingLength);
                    Console.WriteLine("    Pulse Value Threshold:             {0}", pklPulseValueThreshold);
                    Console.WriteLine("    Pulse Derivative Threshold Rising: {0}", pklPulseDerivativeThresholdRising);
                    Console.WriteLine("    Pulse Derivative Threshold Falling:{0}", pklPulseDerivativeThresholdFalling);
                    Console.WriteLine("    Pulse Derivative Hysteresis:       {0}", pklPulseDerivativeHysteresis);
                    Console.WriteLine("    Baseline:                          {0}", pklBaseline);
                    driver.Channels["Channel1"].PeakList.ValueSmoothingLength = pklValueSmoothingLength;
                    driver.Channels["Channel1"].PeakList.DerivativeSmoothingLength = pklDerivativeSmoothingLength;
                    driver.Channels["Channel1"].PeakList.PulseValueThreshold = pklPulseValueThreshold;
                    driver.Channels["Channel1"].PeakList.PulseDerivativeThresholdRising = pklPulseDerivativeThresholdRising;
                    driver.Channels["Channel1"].PeakList.PulseDerivativeThresholdFalling = pklPulseDerivativeThresholdFalling;
                    driver.Channels["Channel1"].PeakList.PulseDerivativeHysteresis = pklPulseDerivativeHysteresis;
                    driver.Channels["Channel1"].PeakList.Baseline = pklBaseline;

                    // Configure the trigger.
                    const string activeSource = "Internal1";
                    const double level = 0.0;
                    const TriggerSlope slope = TriggerSlope.Positive;
                    Console.WriteLine("Configuring trigger\n");
                    Console.WriteLine("    Active Source:               {0}", activeSource);
                    Console.WriteLine("    Level:     {0}", level);
                    Console.WriteLine("    Slope:     {0}", slope);
                    driver.Trigger.ActiveSource = activeSource;
                    driver.Trigger.Sources[activeSource].Level = level;
                    driver.Trigger.Sources[activeSource].Edge.Slope = slope;

                    // Calibrate the instrument.
                    Console.WriteLine("Performing self-calibration\n");
                    driver.Calibration.SelfCalibrate();

                    const string streamName = "PeaksCh1";
                    const long maxElementsToFetchAtOnce = 1024 * 1024;

                    IAqMD3StreamElements<int> peaksStreamElements = driver.Acquisition.CreateStreamElementsInt32(maxElementsToFetchAtOnce);

                    long totalMarkerElements = 0;

                    // Initiate the acquisition.
                    Console.WriteLine("Initiating acquisition\n");
                    driver.Acquisition.Initiate();

                    using (var outputFile = new StreamWriter("TriggeredCountPeakList.log"))
                    {
                        // Wait for acquisition to complete, fetch marker data simultaneously.
                        for (; driver.Acquisition.Status.IsIdle != AcquisitionStatusResult.True;)
                        {
                            // after a wait, fetch all available elements.
                            FetchAvailableElements(driver.Streams[streamName], maxElementsToFetchAtOnce, ref peaksStreamElements);
                            totalMarkerElements += peaksStreamElements.ActualElements;

                            if (peaksStreamElements.ActualElements > 0)
                            {
                                Console.WriteLine("Fetched {0} elements from {1} stream. Remaining elements: {2}", peaksStreamElements.ActualElements, streamName, peaksStreamElements.AvailableElements);
                                PrintMarkers(peaksStreamElements, outputFile);
                            }
                            else
                            {
                                Console.WriteLine("wait for data\n");
                                const int dataWaitTime = 200;
                                Thread.Sleep(dataWaitTime);
                            }
                        }

                        // acquisition is complete, read remaining markers.
                        for (;;)
                        {
                            FetchAvailableElements(driver.Streams[streamName], maxElementsToFetchAtOnce, ref peaksStreamElements);
                            totalMarkerElements += peaksStreamElements.ActualElements;

                            if (peaksStreamElements.ActualElements > 0)
                            {
                                Console.WriteLine("Fetched {0} elements from {1} stream. Remaining elements: {2}", peaksStreamElements.ActualElements, streamName, peaksStreamElements.AvailableElements);
                                PrintMarkers(peaksStreamElements, outputFile);
                            }
                            else
                            {
                                if(peaksStreamElements.AvailableElements != 0)
                                    throw new Exception(String.Format("Fetch returned empty buffer while instrument indicated {0} remaining elements", peaksStreamElements.AvailableElements));

                                Console.WriteLine("No additional markers");
                                break;
                            }
                        }
                    }

                    long totalMarkerData = totalMarkerElements * sizeof(Int32);
                    Console.WriteLine("Total data read: {0} MBytes.", totalMarkerData / (1024 * 1024));

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
        /// Expands the sign of "value" which is a "nbrBits"-bit signed integer to get a 64-bit signed integer.
        /// </summary>
        /// <param name="value"> unsigned representation of a "nbrBits"-bit signed integer.</param>
        /// <param name="nbrBits"> Number of effective bits of "value"</param>
        /// <returns> A signed 64-bit integer value corresponding to the representation of "value"</returns>
        private static Int64 ExpandSign(Int64 value, int nbrBits)
        {
            if (nbrBits <= 0 || 64 <= nbrBits)
                throw new ArgumentException(String.Format("Invalid number of bits for sign expansion: {0}", nbrBits));

            if (value >= (((Int64)1) << (nbrBits - 1)))
                return value - (((Int64)1) << nbrBits);

            return value;
        }

        /// <summary>
        /// Expands the sign of "value" which is a "nbrBits"-bit signed integer to get a 32-bit signed integer.
        /// </summary>
        /// <param name="value"> unsigned representation of a "nbrBits"-bit signed integer.</param>
        /// <param name="nbrBits"> Number of effective bits of "value"</param>
        /// <returns> A signed 32-bit integer value corresponding to the representation of "value"</returns>
        private static Int32 ExpandSign(Int32 value, int nbrBits)
        {
            if (nbrBits <= 0 || 32 <= nbrBits)
                throw new ArgumentException(String.Format("Invalid number of bits for sign expansion: {0}", nbrBits));

            if (value >= (((Int32)1) << (nbrBits - 1)))
                return value - (1 << nbrBits);
            return value;
        }

        /// <summary>
        /// Convert fixed-point representation of a signed value into a (double precision) float-point representation.
        /// </summary>
        /// <param name="value"> the value to scale. It is the integer representation of fixed-point.</param>
        /// <param name="nbrIntegerBits"> the number of integer bits of the fixed-point representation.</param>
        /// <param name="nbrFractionBits"> the number of fraction bits of the fixed-point representation.</param>
        /// <returns> a float-point representation of the given "value". </returns>
        private static double ScaleSigned(Int32 value, int nbrIntegerBits, int nbrFractionBits)
        {
            double scaleFactor = ((double)1.0) / (1 << nbrFractionBits);
            return (double)(ExpandSign(value, nbrIntegerBits + nbrFractionBits)) * scaleFactor;
        }

        /// <summary>
        /// Expect a trigger marker on "peakElements" buffer at the given "offset", decode it and print its content into the #outputFile stream.
        /// </summary>
        /// <param name="peakElements"> The buffer of peak elements </param>
        /// <param name="offset"> The offset in "peakElements" to decode marker from.</param>
        /// <param name="outputFile"> The output file</param
        private static void PrintTriggerMarker(IAqMD3StreamElements<int> peakElements, int offset, StreamWriter outputFile)
        {
            int header = peakElements[offset];
            int tag = header & 0xff;

            if (tag != 0x11)
                throw new ArgumentException(String.Format("Expected trigger marker tag, got {0}", tag));

            int low = peakElements[offset + 1];
            int high = peakElements[offset + 2];

            int recordIndex = (header >> 8) & 0x00ffffff;

            double triggerSubsamplePosition = -((double)(low & 0x000000ff) / 256.0);
            UInt64 trigSampleLow = (((UInt64)low) >> 8) & 0x0000000000ffffffL;
            UInt64 trigSampleHigh = ((UInt64)high) << 24;
            UInt64 triggerSampleIndex = trigSampleHigh | trigSampleLow;

            outputFile.WriteLine("Trigger marker: record #{0}, trigger sample index = {1}, subsample = {2}", recordIndex, triggerSampleIndex, triggerSubsamplePosition);
        }

        /// <summary>
        /// Expect a pulse marker on "peakElements" buffer at the given "offset", decode it and print its content into the #outputFile stream.
        /// </summary>
        /// <param name="peakElements"> The buffer of peak elements </param>
        /// <param name="offset"> The offset in "peakElements" to decode marker from.</param>
        /// <param name="outputFile"> The output file</param
        private static void PrintPulseMarker(IAqMD3StreamElements<int> peakElements, int offset, StreamWriter outputFile)
        {
            int header = peakElements[offset];
            int tag = header & 0xff;

            if (tag != 0x14)
                throw new ArgumentException(String.Format("Expected pulse marker tag, got {}", tag));

            //1. record index
            int recordIndex = (Int32)((header >> 8) & 0x00ffffff);

            //2. timestamp
            int item1 = peakElements[offset+1];
            int item2 = peakElements[offset+2];

            Int64 tsLow = ((Int64)item1) & 0x00000000ffffffffL;
            Int64 tsHigh = ((Int64)item2) & 0x000000000000ffffL;
            Int64 unsignedTimestamp = tsLow | (tsHigh << 32);

            // timestamp is signed and might be negative when pulse is detected before the trigger
            Int64 timestamp = ExpandSign(unsignedTimestamp, 48);

            // 3. width
            int width = (item2 >> 16) & 0x00007fff;

            //4. overflow
            bool overflow = (((item2 >> 31) & 0x01) != 0);

            //5. number of overrange samples
            Int32 item3 = peakElements[offset + 3];
            Int32 nbrOverrangeSamples = item3 & 0x00007fff;

            //6. sum of squares
            Int64 soSLow = (((Int64)item3) >> 16) & 0x000000000000ffffL;
            Int32 item4 = peakElements[offset + 4];
            Int64 soSHigh = ((Int64)item4) & 0x00000000ffffffffL;

            Int64 sumOfSquares = (soSHigh << 16) | soSLow;

            //7. peak position
            Int32 item5 = peakElements[offset + 5];
            Int32 item6 = peakElements[offset + 6];

            int peakXRaw = item5 & 0x00ffffff;
            int peakYRaw = ((item5 >> 24) & 0x000000ff) | ((item6 & 0x0000ffff) << 8);

            //8. center of mass position
            Int32 item7 = peakElements[offset + 7];
            int comXRaw = ((item6 >> 16) & (0x0000ffff)) | ((item7 & 0x000000ff) << 16);
            int comYRaw = (item7 >> 8) & 0x00ffffff;

            /*9. Scale fixed-point representations (peak & center of mass coordinates) into float-point values.
                 Please refer to the User Manual (section "Real-time peak-listing mode (PKL option)") for more
                 details on the layout of fixed-point representations associated with the following fields:
                 Peak timestamp, peak value, center-of-mass timestamp and center-of-mass value.*/
            const int peakX_nbrIntegerBits = 14;
            const int peakX_nbrFractionalBits = 8;
            const int peakY_nbrIntegerBits = 17;
            const int peakY_nbrFractionalBits = 3;

            const int comX_nbrIntegerBits = 16;
            const int comX_nbrFractionalBits = 8;
            const int comY_nbrIntegerBits = 16;
            const int comY_nbrFractionalBits = 1;

            double peakX = ScaleSigned(peakXRaw, peakX_nbrIntegerBits, peakX_nbrFractionalBits);
            double peakY = ScaleSigned(peakYRaw, peakY_nbrIntegerBits, peakY_nbrFractionalBits);

            double comX = ScaleSigned(comXRaw, comX_nbrIntegerBits, comX_nbrFractionalBits);
            double comY = ScaleSigned(comYRaw, comY_nbrIntegerBits, comY_nbrFractionalBits);

            // print the content of the marker into output stream.
            outputFile.WriteLine("     - Pulse descriptor:");
            outputFile.WriteLine("            - Record index                                      : {0}", recordIndex);
            outputFile.WriteLine("            - Timestamp (rel. to record's 1st sample)           : {0} {1}", timestamp, timestamp < 0 ? " (the pulse starts before the trigger)" : "");

            outputFile.WriteLine("            - Width (in samples)                                : {0}", width);
            outputFile.WriteLine("            - Overrange samples                                 : {0}", nbrOverrangeSamples);

            outputFile.WriteLine("            - Peak timestamp (rel. to the 1st pulse sample)     : {0}", peakX);
            outputFile.WriteLine("            - Peak value (16-bit ADC code)                      : {0}", peakY);

            outputFile.WriteLine("            - Sum of Squares (rel. to baseline, ADC code^2)     : {0}", sumOfSquares);

            outputFile.WriteLine("            - Center of mass (rel. to the 1st pulse sample)     : {0}", comX);
            outputFile.WriteLine("            - Center of mass value (rel. to baseline, ADC code) : {0}", comY);

        }

        /// <summary>
        /// Decode all markers contained in "peakElements" buffer and print them into the #outputFile stream.
        /// </summary>
        private static void PrintMarkers(IAqMD3StreamElements<int> peakElements, StreamWriter outputFile)
        {
            const int nbrMarkerElements = 8;
            for(int offset =0; offset < peakElements.ActualElements; offset += nbrMarkerElements)
            {
                int tag = peakElements[offset] & 0xff;

                switch(tag)
                {
                    case 0x11: // trigger marker
                        PrintTriggerMarker(peakElements, offset, outputFile);
                        break;
                    case 0x14: // pulse marker
                        PrintPulseMarker(peakElements, offset, outputFile);
                        break;
                    case 0x1f: // alignment marker
                        outputFile.WriteLine("\n     - alignment marker.");
                        break;
                    default:
                        throw new Exception(String.Format("Unexpected tag {0}", tag));
                }
            }
        }

    }
}
