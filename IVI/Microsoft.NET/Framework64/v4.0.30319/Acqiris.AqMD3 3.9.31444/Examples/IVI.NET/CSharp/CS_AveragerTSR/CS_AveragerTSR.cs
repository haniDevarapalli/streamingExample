///
/// Acqiris IVI.NET Driver C# Example Program
///
/// Initializes the driver, reads a few Identity interface properties, and performs
/// accumulated acquisitions using TSR.
///
/// For additional information on programming with IVI drivers in various IDEs, please see
/// http://www.ivifoundation.org/resources/
///
/// WARNING:
/// The Averager TSR features are not supported in simulation mode. You will have to update
/// the resource string (resource[]) to match your configuration and disable the
/// simulation mode (options[] - set Simulate=false) to be able to run this example
/// successfully.
///

using System;
using Acqiris.AqMD3;

namespace CS_AveragerTSR
{
    class CS_AveragerTSR
    {
        static void Main(string[] args)
        {
            Console.WriteLine("  CS_AveragerTSR");
            Console.WriteLine();

            // Edit resource and options as needed. Resource is ignored if option Simulate=true.
            string resourceDesc = "PXI40.0.0.INSTR";

            // An input signal is necessary if the example is run in non simulated mode, otherwise
            // the acquisition will time out. Set the Simulate option to false to disable simulation.
            string initOptions = "Simulate=true, DriverSetup= Model=U5309A";

            bool  idquery = false;
            bool  reset = false;

            try
            {
                // Initialize the driver. See driver help topic "Initializing the IVI.NET Driver" for additional information.
                AqMD3 driver = new AqMD3(resourceDesc, idquery, reset, initOptions);

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
                if (!driver.InstrumentInfo.Options.Contains("AVG") || !driver.InstrumentInfo.Options.Contains("TSR"))
                {
                    Console.WriteLine("Both AVG and TSR module options are required on the instrument.");
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


                // Configure the channel properties.
                double range = 1.0;
                double  offset = 0.0;
                VerticalCoupling coupling = VerticalCoupling.DC;
                Console.WriteLine("Configuring channel properties");
                Console.WriteLine("Range:              {0}", range);
                Console.WriteLine("Offset:             {0}", offset);
                Console.WriteLine("Coupling:           {0}", coupling);
                driver.Channels["Channel1"].Configure(range, offset, coupling, true);

                // Configure the acquisition.
                long numRecords = 8;
                long numPointsPerRecord = 1600;
                int numAverages = 80;
                Console.WriteLine("Configuring acquisition");
                Console.WriteLine("Number of Averages: {0}", numAverages);
                driver.Acquisition.NumberOfAverages = numAverages;
                Console.WriteLine("Record size:        {0}", numPointsPerRecord);
                driver.Acquisition.RecordSize = numPointsPerRecord;

                // Have to enable time-interleaving in order to enable TSR with AVG on U5303A
                if (driver.Identity.InstrumentModel == "U5303A")
                    driver.Channels["Channel1"].TimeInterleavedChannelList = "Channel2";

                Console.WriteLine("Mode:               Averager");
                driver.Acquisition.Mode = AcquisitionMode.Averager;
                Console.WriteLine("TSR:                Enabled");
                driver.Acquisition.TSR.Enabled = true;
                Console.WriteLine("Number of records:  {0}", numRecords);
                driver.Acquisition.NumberOfRecordsToAcquire = numRecords;

                // Configure the trigger.
                // Warning: The self-trigger source is not supported by all models. Please refer to the User Manual
                //          for more information.
                Console.WriteLine("Configuring trigger (self-trigger)");
                double squareWaveFrequency = 10e3; // 10 KHz
                double squareWaveDutyCycle = 10.2; // 10.2% of the period - maximum autorized with frequency of 10 KHz
                TriggerSlope squareWaveSlope = TriggerSlope.Positive;
                Console.WriteLine("Frequency:          {0}", squareWaveFrequency);
                Console.WriteLine("Duty cycle:         {0}", squareWaveDutyCycle);
                Console.WriteLine("Slope:              {0}\n", squareWaveSlope);
                driver.ControlIOs["ControlIO3"].Signal = "Out-AveragerAwg"; // Shunt self-trigger signal to the control IO 3 output.
                driver.Trigger.ActiveSource = "SelfTrigger";
                IAqMD3TriggerSource  selfTriggerSource = driver.Trigger.Sources["SelfTrigger"];
                selfTriggerSource.SelfTrigger.Mode = SelfTriggerMode.SquareWave;
                selfTriggerSource.SelfTrigger.SquareWave.Configure(squareWaveFrequency, squareWaveDutyCycle, squareWaveSlope);


                // Calibrate the instrument.
                Console.WriteLine("Performing self-calibration");
                driver.Calibration.SelfCalibrate();

                // Start SelfTrigger signal generation.
                Console.WriteLine("Performing acquisition");
                selfTriggerSource.SelfTrigger.InitiateGeneration();

                // Initiate Acquisition
                driver.Acquisition.Initiate();

                // Recover acquired data.
                long  firstRecord = 0;
                long  offsetWithinRecord = 0;

                // Pre-allocate waveform collection to avoid performance impact on the very first fetch.
                IAqMD3AccumulatedWaveformCollection<Int32>  waveforms = driver.Acquisition.CreateAccumulatedWaveformCollectionInt32(numRecords, numPointsPerRecord);

                int  numberOfLoops = 50;
                for (int loop = 0; loop < numberOfLoops; ++loop)
                {
                    // Poll for trigger events.
                    int timeoutInMs = 1000;
                    int timeCounter = 0;
                    while (timeCounter < timeoutInMs)
                    {
                        if (driver.Acquisition.TSR.IsAcquisitionComplete)
                            break;

                        System.Threading.Thread.Sleep(1);
                        ++timeCounter;
                    }

                    // Check for timeout.
                    if (timeCounter >= timeoutInMs)
                    {
                        throw new Exception("A timeout occurred while waiting for trigger during TSR acquisition.");
                    }

                    // Fetch acquired data.
                    waveforms = driver.Channels["Channel1"].Measurement.FetchAccumulatedWaveform(firstRecord, numRecords, offsetWithinRecord, numPointsPerRecord, waveforms);

                    // Release the corresponding memory and mark it as available for new acquisitions.
                    driver.Acquisition.TSR.Continue();

                    // Cache the state of memory overflow.
                    bool  tsrOverflow = driver.Acquisition.TSR.MemoryOverflowOccurred;

                    Console.WriteLine("Processing data loop {0}", loop);
                    foreach(var waveform in waveforms)
                    {
                        for (int point = 0; point < waveform.ValidPointCount; ++point)
                        {
                            double sampleInVolts = waveform.GetScaled(point);
                        }
                    }

                    // Display trigger-time delta (in seconds)
                    for (int i = 1; i < waveforms.ValidWaveformCount; ++i)
                    {
                        double deltaInSeconds = (waveforms[i].TriggerTime - waveforms[i - 1].TriggerTime).TotalSeconds;
                        Console.WriteLine("    - Trigger time difference ( record#{0} - record#{1} ) = {2} s", i, i - 1, deltaInSeconds);
                    }

                    Console.WriteLine("Processing completed");

                    if (tsrOverflow)
                        throw new Exception("A memory overflow occurred during TSR acquisition.");
                }

                // Stop the SelfTrigger signal
                selfTriggerSource.SelfTrigger.AbortGeneration();
                //Stop the acquisition
                driver.Acquisition.Abort();

                // Close the driver.
                driver.Close();
                Console.WriteLine("Driver closed");
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
