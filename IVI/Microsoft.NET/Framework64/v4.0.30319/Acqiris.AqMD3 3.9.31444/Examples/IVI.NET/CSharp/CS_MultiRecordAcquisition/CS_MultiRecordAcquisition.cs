///
/// Acqiris IVI.NET AqMD3 Driver Example Program
///
/// Creates a driver object, reads a few Identity interface properties, and performs a multi-
/// record acquisition.
///
/// For additional information on programming with IVI drivers in various IDEs, please see
/// http://www.ivifoundation.org/resources/
///
/// Runs in simulation mode without an instrument.
///
/// Requires a .NET reference to the driver's type library.
///

using System;
using Acqiris.AqMD3;

namespace CS_MultiRecordAcquisition
{
    public class App
    {
        [STAThread]
        public static void Main(string[] rgs)
        {
            Console.WriteLine("  CS_MultiRecordAcquisition");
            Console.WriteLine();

            // Edit resource and options as needed. Resource is ignored if option Simulate=true.
            string resourceDesc = "PXI40::0::0::INSTR";

            // An input signal is necessary if the example is run in non simulated mode,
            // otherwise the acquisition will time out.
            string initOptions = "Simulate=true, DriverSetup= Model=U5303A";

            bool idquery = false;
            bool reset = false;

            try
            {
                // Initialize the driver. See driver help topic "Initializing the IVI.NET Driver" for additional information.
                using (var driver = new AqMD3(resourceDesc, idquery, reset, initOptions))
                {
                    Console.WriteLine("Driver initialized");

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

                    // Configure channel properties.
                    double range = 1.0;
                    double offset = 0.0;
                    VerticalCoupling coupling = VerticalCoupling.DC;

                    Console.WriteLine();
                    Console.WriteLine("Configuring channel properties");
                    Console.WriteLine("Range:              {0}", range);
                    Console.WriteLine("Offset:             {0}", offset);
                    Console.WriteLine("Coupling:           {0}", coupling);
                    foreach (IAqMD3Channel channel in driver.Channels)
                    {
                        Console.WriteLine("Applying on {0}", channel.Name);
                        channel.Configure(range, offset, coupling, true);
                    }

                    // Configure Acquisition
                    long numRecords = 20;
                    long numPointsPerRecord = 1000000;

                    Console.WriteLine();
                    Console.WriteLine("Configuring acquisition");
                    Console.WriteLine("Number of records:  {0}", numRecords);
                    Console.WriteLine("Record size:        {0}", numPointsPerRecord);
                    driver.Acquisition.NumberOfRecordsToAcquire = numRecords;
                    driver.Acquisition.RecordSize = numPointsPerRecord;

                    // Configure the trigger.
                    string sourceName = "Internal1";
                    double level = 0.0;
                    TriggerSlope slope = TriggerSlope.Positive;

                    Console.WriteLine();
                    Console.WriteLine("Configuring trigger");
                    Console.WriteLine("Active source:      {0}", sourceName);
                    driver.Trigger.ActiveSource = sourceName;
                    IAqMD3TriggerSource activeTrigger = driver.Trigger.Sources[sourceName];
                    Console.WriteLine("Level:              {0}", level);
                    activeTrigger.Level = level;
                    Console.WriteLine("Slope:              {0}", slope);
                    activeTrigger.Edge.Slope = slope;

                    // Calibrate the instrument.
                    Console.WriteLine();
                    Console.WriteLine("Performing self-calibration");
                    driver.Calibration.SelfCalibrate();

                    // Perform the acquisition.
                    Console.WriteLine("Performing acquisition");
                    driver.Acquisition.Initiate();
                    int timeoutInMs = 1000;
                    driver.Acquisition.WaitForAcquisitionComplete(timeoutInMs);
                    Console.WriteLine("Acquisition completed");

                    Console.WriteLine("Processing data");
                    long offsetWithinRecord = 0;
                    long firstRecord = 0;
                    foreach (IAqMD3Channel channel in driver.Channels)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Fetching data from {0}", channel.Name);

                        Ivi.Digitizer.IWaveformCollection<short> waveforms = null; // Will be allocated during the fetch.

                        // Giving a null pointer as waveform collection to the fetch function means the driver will allocate the proper amount of memory during
                        // the fetch call.
                        waveforms = channel.MultiRecordMeasurement.FetchMultiRecordWaveform(firstRecord,
                            numRecords,
                            offsetWithinRecord,
                            numPointsPerRecord,
                            waveforms);

                        Console.WriteLine("Processing data fetched from {0}", channel.Name);
                        // Convert data to Volts for each record.
                        foreach (Ivi.Driver.IWaveform<short> waveform in waveforms)
                        {
                            double[] dataPerRecordInVolts = new double[waveform.ValidPointCount];

                            for (int point = 0; point < waveform.ValidPointCount; ++point)
                                dataPerRecordInVolts[point] = waveform.GetScaled(point);
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
