///
/// Acqiris IVI.NET Driver Example Program
///
/// For additional information on programming with IVI drivers in various IDEs, please see
/// http://www.ivifoundation.org/resources/
///
/// WARNING:
/// The Averager features are not supported in simulation mode. You will have to update
/// the resource string (resourceDesc) to match your configuration and disable the
/// simulation mode (initOptions - set Simulate=false) to be able to run this example
/// successfully.
///

using System;
using Acqiris.AqMD3;


namespace CS_Averager
{
    class CS_Averager
    {
        static void Main(string[] args)
        {
            Console.WriteLine("  CS_Averager");
            Console.WriteLine();

            // Edit resource and options as needed. Resource is ignored if option Simulate=true.
            string resourceDesc = "PXI40::0::0::INSTR";

            // An input signal is necessary if the example is run in non simulated mode, otherwise
            // the acquisition will time out. Set the Simulate option to false to disable simulation.
            string initOptions = "Simulate=true, DriverSetup= Model=U5303A";

            bool idquery = false;
            bool reset = false;

            try
            {
                // Initialize the driver. See driver help topic "Initializing the IVI.NET Driver" for additional information.
                using (var driver = new AqMD3(resourceDesc, idquery, reset, initOptions))
                {
                    Console.WriteLine("Driver initialized");

                    // Abort execution if instrument is still in simulated mode.
                    if (driver.DriverOperation.Simulate)
                    {
                        Console.WriteLine("The AVG features are not supported in simulated mode.");
                        Console.WriteLine("Please update the resource string (resourceDesc) to match your configuration, and update the init options string (initOptions) to disable simulation.");
                        Console.WriteLine("Interrupted - Press enter to exit");
                        Console.Read();
                        System.Environment.Exit(-1);
                    }

                    // Check the instrument contains the required AVG module option.
                    if (!driver.InstrumentInfo.Options.Contains("AVG"))
                    {
                        Console.WriteLine("The required AVG module option is missing from the instrument.");
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

                    Console.WriteLine("Configuring acquisition");
                    // Configure the acquisition.
                    double range = 1.0;
                    double offset = 0.0;
                    VerticalCoupling coupling = VerticalCoupling.DC;
                    Console.WriteLine("Configuring acquisition");
                    Console.WriteLine("Range:              {0}", range);
                    Console.WriteLine("Offset:             {0}", offset);
                    Console.WriteLine("Coupling:           {0}", coupling);
                    driver.Channels["Channel1"].Configure(range, offset, coupling, true);
                    long numRecords = 1;
                    long numPointsPerRecord = 1600;
                    int numAverages = 80;
                    Console.WriteLine("Number of records:  {0}", numRecords);
                    Console.WriteLine("Number of Averages: {0}\n", numAverages);
                    Console.WriteLine("Record size:        {0}\n", numPointsPerRecord);
                    driver.Acquisition.NumberOfRecordsToAcquire = numRecords;
                    driver.Acquisition.RecordSize = numPointsPerRecord;
                    driver.Acquisition.NumberOfAverages = numAverages;
                    driver.Acquisition.Mode = AcquisitionMode.Averager;

                    // Configure the trigger.
                    // Warning: The self-trigger source is not supported by all models. Please refer to the User Manual
                    //          for more information.
                    Console.WriteLine("Configuring trigger");
                    const string activeSource = "External1";
                    const double triggerLevel= 0.5;
                    const TriggerSlope triggerSlope = TriggerSlope.Positive;
                    Console.WriteLine("Active Source: {0}", activeSource);
                    Console.WriteLine("Trigger Level: {0}", triggerLevel);
                    Console.WriteLine("Trigger Slope: {0}\n", triggerSlope);

                    driver.Trigger.ActiveSource = activeSource;
                    driver.Trigger.Sources[activeSource].Edge.Configure(triggerLevel, triggerSlope);

                    // Calibrate the instrument.
                    Console.WriteLine("Performing self-calibration");
                    driver.Calibration.SelfCalibrate();

                    // Perform the acquisition.
                    Console.WriteLine("Performing acquisition");
                    int timeoutInMs = 1000;
                    driver.Acquisition.Initiate();
                    driver.Acquisition.WaitForAcquisitionComplete(timeoutInMs);

                    Console.WriteLine("Acquisition completed");

                    // Fetch the acquired data in accumulated waveform.
                    long firstRecord = 0;
                    long offsetWithinRecord = 0;
                    IAqMD3AccumulatedWaveformCollection<Int32> waveforms = null;
                    // Giving a null pointer as data array to the fetch function means the driver will allocate the proper amount of memory during
                    // the fetch call.
                    waveforms = driver.Channels["Channel1"].Measurement.FetchAccumulatedWaveform(firstRecord, numRecords, offsetWithinRecord, numPointsPerRecord, waveforms);
                    Console.WriteLine("Processing data");

                    foreach (var waveform in waveforms)
                    {
                        for (int point = 0; point < waveform.ValidPointCount; ++point)
                        {
                            double sampleInVolts = waveform.GetScaled(point);
                        }
                    }

                    Console.WriteLine("Processing completed");

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
    }
}
