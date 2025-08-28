/// <summary>
/// Acqiris IVI.NET AqMD3 Driver C++/CLI Example Program
///
/// Creates a driver object, reads a few Identity interface properties, and performs a simple
/// acquisition.
///
/// For additional information on programming with IVI drivers in various IDEs, please see
/// http://www.ivifoundation.org/resources/
///
/// Runs in simulation mode without an instrument.
///
/// Requires a .NET reference to the driver's type library.
///
/// </summary>


using namespace System;
using namespace Acqiris::AqMD3;

int main(array<System::String ^> ^args)
{
    Console::WriteLine("  CPP_SimpleAcquisition");
    Console::WriteLine();

    // Edit resource and options as needed. Resource is ignored if option Simulate=true.
    System::String^ resourceDesc = "PXI40::0::0::INSTR";

    System::String^ initOptions = "Simulate=true, DriverSetup= Model=U5303A";

    bool idquery = false;
    bool reset = false;

    try
    {
        AqMD3^ driver = gcnew AqMD3(resourceDesc, idquery, reset, initOptions);

        Console::WriteLine("Driver initialized");

        // Print a few IIviDriverIdentity properties.
        Console::WriteLine("Driver identifier:  {0}", driver->Identity->Identifier);
        Console::WriteLine("Driver revision:    {0}", driver->Identity->Revision);
        Console::WriteLine("Driver vendor:      {0}", driver->Identity->Vendor);
        Console::WriteLine("Driver description: {0}", driver->Identity->Description);
        Console::WriteLine("Instrument model:   {0}", driver->Identity->InstrumentModel);
        Console::WriteLine("Firmware revision:  {0}", driver->Identity->InstrumentFirmwareRevision);
        Console::WriteLine("Serial number:      {0}", driver->InstrumentInfo->SerialNumberString);
        Console::WriteLine("Options:            {0}", driver->InstrumentInfo->Options);
        Console::WriteLine("Simulate:           {0}", driver->DriverOperation->Simulate);

        // Configure channel properties.
        double range = 1.0;
        double offset = 0.0;
        VerticalCoupling coupling = VerticalCoupling::DC;

        Console::WriteLine();
        Console::WriteLine("Configuring channel properties");
        Console::WriteLine("Range:              {0}", range);
        Console::WriteLine("Offset:             {0}", offset);
        Console::WriteLine("Coupling:           {0}", coupling);
        for each(IAqMD3Channel^ channel in driver->Channels)
        {
            Console::WriteLine("Applying on {0}", channel->Name);
            channel->Configure(range, offset, coupling, true);
        }

        // Configure the acquisition.
        long numPointsPerRecord = 1000000;
        Console::WriteLine();
        Console::WriteLine("Configuring acquisition");
        Console::WriteLine("Record size:        {0}", numPointsPerRecord);
        driver->Acquisition->RecordSize = numPointsPerRecord;

        // Configure the trigger.
        String^ sourceName = "Internal1";
        double level = 0.0;
        TriggerSlope slope = TriggerSlope::Positive;

        Console::WriteLine();
        Console::WriteLine("Configuring trigger");
        Console::WriteLine("Active source:      {0}", sourceName);
        driver->Trigger->ActiveSource = sourceName;
        IAqMD3TriggerSource^ activeTrigger = driver->Trigger->Sources[sourceName];
        Console::WriteLine("Level:              {0}", level);
        activeTrigger->Level = level;
        Console::WriteLine("Slope:              {0}", slope);
        activeTrigger->Edge->Slope = slope;


        // Calibrate the instrument.
        Console::WriteLine();
        Console::WriteLine("Performing self-calibration");
        driver->Calibration->SelfCalibrate();

        // Perform the acquisition.
        Console::WriteLine("Performing acquisition");
        driver->Acquisition->Initiate();
        int timeoutInMs = 1000;
        driver->Acquisition->WaitForAcquisitionComplete(timeoutInMs);
        Console::WriteLine("Acquisition completed");

        for each(IAqMD3Channel^ channel in driver->Channels)
        {
            Console::WriteLine();
            Console::WriteLine("Fetching data from {0}", channel->Name);
            // Fetch the acquired data
            Ivi::Driver::IWaveform<short>^ waveform = nullptr;
            /* Giving a null pointer as waveform to the fetch function means the driver will allocate the proper amount of memory during
               the fetch call.*/
            waveform = channel->Measurement->FetchWaveform(waveform);

            Console::WriteLine("Processing data fetched from {0}", channel->Name);
            // Convert data to Volts.
            for (int point = 0; point < waveform->ValidPointCount; ++point)
            {
                // raw sample
                short const sampleRaw = waveform[point];
                // the same sample in volts
                double const sampleInVolts = waveform->GetScaled(point);
                // manual computation of the sample in volts
                double const sampleInVolts2 = sampleRaw * waveform->Scale + waveform->Offset;

                System::Diagnostics::Debug::Assert(sampleInVolts == sampleInVolts2);
            }
        }

        Console::WriteLine("Processing completed. ");

        // Close the driver.
        driver->Close();
        Console::WriteLine("Driver closed");
    }
    catch(System::Exception^ ex)
    {
        Console::WriteLine(ex->Message);
        return 1;
    }

    Console::Write("\nDone - Press enter to exit");
    Console::ReadLine();

    return 0;
}
