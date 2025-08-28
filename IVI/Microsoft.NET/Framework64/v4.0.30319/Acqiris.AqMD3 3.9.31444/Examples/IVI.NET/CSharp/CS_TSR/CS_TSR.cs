////// Acqiris IVI.NET Driver Example Program
///
/// Creates a driver object, reads a few Identity interface properties, and performs a
/// TSR acquisition.
///
/// For additional information on programming with IVI drivers in various IDEs, please see
/// http://www.ivifoundation.org/resources/
/// 
/// WARNING:
/// The TSR features are not supported in simulation mode. You will have to update
/// the resource string (resourceDesc) to match your configuration and disable the
/// simulation mode (initOptions - set Simulate=false) to be able to run this example
/// successfully.
///

using System;
using System.Threading;
using Acqiris.AqMD3;

namespace CS_TSR
{
    class CS_TSR
    {
        static void Main(string[] args)
        {
            Console.WriteLine("  CS_TSR\n");

            // Edit resource and options as needed. Resource is ignored if option Simulate=true.
            string resourceDesc = "PXI40::0::0::INSTR";

            // An input signal is necessary if the example is run in non simulated mode, otherwise
            // the acquisition will time out. Set the Simulate option to false to disable simulation.
            string initOptions = "Simulate=true, DriverSetup= Model=U5303A";

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
                        Console.WriteLine("The TSR features are not supported in simulated mode.");
                        Console.WriteLine("Please update the resource string (strResourceDesc) to match your configuration, and update the init options string (strInitOptions) to disable simulation.");
                        Console.WriteLine("Interrupted - Press enter to exit");
                        Console.ReadLine();
                    }

                    // Check the instrument contains the required TSR module option.
                    if (!driver.InstrumentInfo.Options.Contains("TSR"))
                    {
                        Console.WriteLine("The required TSR module option is missing from the instrument.");
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

                    // Configure the acquisition.
                    double range = 1.0;
                    double offset = 0.0;
                    VerticalCoupling coupling = VerticalCoupling.DC;
                    Console.WriteLine("Configuring acquisition");
                    Console.WriteLine("Range:              {0}", range);
                    Console.WriteLine("Offset:             {0}", offset);
                    Console.WriteLine("Coupling:           {0}", coupling);
                    driver.Channels["Channel1"].Configure(range, offset, coupling, true);
                    long numRecords = 20;
                    long numPointsPerRecord = 1600;
                    Console.WriteLine("Number of records:  {0}", numRecords);
                    Console.WriteLine("Record size:        {0}\n", numPointsPerRecord);
                    driver.Acquisition.NumberOfRecordsToAcquire = numRecords;
                    driver.Acquisition.RecordSize = numPointsPerRecord;
                    driver.Acquisition.TSR.Enabled = true;

                    // Configure the trigger.
                    Console.WriteLine("Configuring trigger\n");
                    driver.Trigger.ActiveSource = "Internal1";

                    // Calibrate the instrument.
                    Console.WriteLine("Performing self-calibration\n");
                    driver.Calibration.SelfCalibrate();

                    // Initiate the acquisition.
                    Console.WriteLine("Initiating acquisition\n");
                    driver.Acquisition.Initiate();

                    // Recover acquired data.
                    long firstRecord = 0;
                    long offsetWithinRecord = 0;
                    // Pre-allocate waveform collection to avoid performance impact on the very first fetch.
                    Ivi.Digitizer.IWaveformCollection<short> waveforms = driver.Acquisition.CreateWaveformCollectionInt16(numRecords, numPointsPerRecord);

                    int numberOfLoops = 50;
                    for (int loop = 0; loop < numberOfLoops; ++loop)
                    {
                        // Check for memory overflow.
                        if (driver.Acquisition.TSR.MemoryOverflowOccurred)
                        {
                            throw new Exception("A memory overflow occurred during TSR acquisition.");
                        }

                        // Poll for trigger events.
                        int timeoutInMs = 1000;
                        int timeCounter = 0;
                        while (timeCounter < timeoutInMs)
                        {
                            if (driver.Acquisition.TSR.IsAcquisitionComplete)
                                break;

                            Thread.Sleep(1);
                            ++timeCounter;
                        }

                        // Check for timeout.
                        if (timeCounter >= timeoutInMs)
                        {
                            throw new Exception("A timeout occurred while waiting for trigger during TSR acquisition.");
                        }

                        // Fetch acquired data.
                        waveforms = driver.Channels["Channel1"].MultiRecordMeasurement.FetchMultiRecordWaveform(firstRecord, numRecords, offsetWithinRecord, numPointsPerRecord, waveforms);

                        // Release the corresponding memory and mark it as available for new acquisitions.
                        driver.Acquisition.TSR.Continue();

                        Console.WriteLine("Processing data loop {0}", loop);
                        foreach (var waveform in waveforms)
                        {
                            for (int point = 0; point < waveform.ValidPointCount; ++point)
                            {
                                double sampleInVolts = waveform.GetScaled(point);
                            }
                        }
                        Console.WriteLine("Processing completed");
                    }

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
    }
}
