'
' Acqiris IVI.NET Driver VB.NET Example Program
'
' Creates a driver object, reads a few Identity interface properties, and performs a simple
' acquisition.
'
' For additional information on programming with IVI drivers in various IDEs, please see
' http://www.ivifoundation.org/resources/
'
' Requires a reference to the driver's type library.
'

Imports Acqiris.AqMD3

Module VB_SimpleAcquisition
    Sub Main()
        Console.WriteLine("  VB_SimpleAcquisition")
        Console.WriteLine()

        ' Edit resource and options as needed. Resource is ignored if option Simulate=true.
        Dim resourceDesc As String = "PXI40::0::0::INSTR"

        ' An input signal is necessary if the example is run in non simulated mode, otherwise
        ' the acquisition will time out.
        Dim initOptions As String = "Simulate=true, DriverSetup= Model=U5303A"

        Dim idquery As Boolean = False
        Dim reset As Boolean = False

        Try

            ' Initialize the driver. See driver help topic "Initializing the IVI.NET Driver" for additional information.
            Dim driver As New AqMD3(resourceDesc, idquery, reset, initOptions)

            Console.WriteLine("Driver initialized")

            ' Get driver Identity properties.
            Console.WriteLine("Driver identifier:  {0}", driver.Identity.Identifier)
            Console.WriteLine("Driver revision:    {0}", driver.Identity.Revision)
            Console.WriteLine("Driver vendor:      {0}", driver.Identity.Vendor)
            Console.WriteLine("Driver description: {0}", driver.Identity.Description)
            Console.WriteLine("Instrument model:   {0}", driver.Identity.InstrumentModel)
            Console.WriteLine("Firmware revision:  {0}", driver.Identity.InstrumentFirmwareRevision)
            Console.WriteLine("Serial number:      {0}", driver.InstrumentInfo.SerialNumberString)
            Console.WriteLine("Options:            {0}", driver.InstrumentInfo.Options)
            Console.WriteLine("Simulate:           {0}", driver.DriverOperation.Simulate)
            Console.WriteLine()

            ' Configure the acquisition.
            Dim range As Double = 1.0
            Dim offset As Double = 0.0
            Dim coupling As VerticalCoupling = VerticalCoupling.DC
            Console.WriteLine("Configuring acquisition")
            Console.WriteLine("Range:              {0}", range)
            Console.WriteLine("Offset:             {0}", offset)
            Console.WriteLine("Coupling:           {0}", coupling)
            driver.Channels("Channel1").Configure(range, offset, coupling, True)
            Dim recordSize As Long = 1000
            Console.WriteLine("Record size:        {0}", recordSize)
            Console.WriteLine()
            driver.Acquisition.RecordSize = recordSize

            ' Configure the trigger.
            Console.WriteLine("Configuring trigger")
            driver.Trigger.ActiveSource = "Internal1"

            ' Calibrate the instrument.
            Console.WriteLine("Performing self-calibration")
            driver.Calibration.SelfCalibrate()

            ' Perform the acquisition.
            Console.WriteLine("Performing acquisition")
            driver.Acquisition.Initiate()
            Dim timeout As Integer = 1000
            driver.Acquisition.WaitForAcquisitionComplete(timeout)
            Console.WriteLine("Acquisition completed")

            ' Fetch the acquired data.
            Dim waveform As Ivi.Driver.IWaveform(Of Int16) = Nothing
            ' Giving a NoThing as waveform to the fetch function means the driver will allocate the proper amount of memory during
            waveform = driver.Channels.Item("Channel1").Measurement.FetchWaveform(waveform)

            ' Convert data to Volts.
            Console.WriteLine("Processing data")
            For point As Integer = CInt(0) To CInt(waveform.ValidPointCount - 1)
                Dim valueInVolts As Double = waveform.GetScaled(point)
            Next
            Console.WriteLine("Processing completed")

            ' Close driver.
            driver.Close()
            Console.WriteLine("Driver closed")

        Catch err As System.Exception
            Console.WriteLine()
            Console.WriteLine("Exception error:")
            Console.WriteLine("  " + err.Message())
        End Try

        Console.WriteLine()
        Console.Write("Done - Press enter to exit ")
        Console.ReadLine()
    End Sub

End Module
