///
/// Acqiris IVI-C Driver Example Program
///
/// Initializes the driver, reads a few Identity interface properties, and performs a
/// TriggeredCount streaming acquisition in PeakList mode.
///
/// For additional information on programming with IVI drivers in various IDEs, please see
/// http://www.ivifoundation.org/resources/
///
/// The Example requires a real instrument having CST and input signal on "Channel1". It
/// also requires PKL option to enable PeakList acquisition mode.
///

#include "../../include/LibTool.h"
using namespace LibTool;
#include "AqMD3.h"

#include <iostream>
using std::cout;
using std::cerr;
using std::hex;
#include <vector>
#include <stdexcept>
#include <chrono>
using std::chrono::minutes;
using std::chrono::seconds;
using std::chrono::milliseconds;
using std::chrono::microseconds;
using std::chrono::system_clock;
#include <thread>
using std::this_thread::sleep_for;
#include <fstream>
#include <algorithm>

#define checkApiCall( f ) do { ViStatus s = f; testApiCall( s, #f ); } while( false )

typedef std::vector<int32_t> FetchBuffer;

//! Validate success status of the given functionName.
void testApiCall( ViStatus status, char const * functionName );

//! Attempt to fetch "nbrElementsToFetch" elements from the "streamName", or the available number of elements if less than.
/*! The resulting array segment might be empty if the fetch fails to read data from the stream.
     \param[in] session: session handle associated with the instrument to read from.
     \param[in] streamName: the stream identifier to read from.
     \param[in] nbrElementsToFetch: the maximum number of elements to read.
     \param[in] buffer: buffer used by fetch for read operation.
     \param[out] nbrRemainingElements: the number of remaining elements on the stream after fetch operation.
     \return the array-segment delimiting actual valid data returned by the instrument. The segment might be empty if no data has been read.*/
ArraySegment<int32_t> FetchAvailableElements(ViSession session, ViConstString streamName, ViInt64 nbrElementsToFetch, FetchBuffer& buffer, ViInt64& nbrRemainingElements);

//! Attempt to fetch "nbrElementsToFetch" elements from the "streamName".
/*! The resulting array segment might be empty if the fetch fails to read data from the stream.
     \param[in] session: session handle associated with the instrument to read from.
     \param[in] streamName: the stream identifier to read from.
     \param[in] nbrElementsToFetch: the maximum number of elements to read.
     \param[in] buffer: buffer used by fetch for read operation.
     \param[out] nbrRemainingElements: the number of remaining elements on the stream after fetch operation.
     \return the array-segment delimiting actual valid data returned by the instrument. The segment might be empty if no data has been read.*/
ArraySegment<int32_t> FetchElements(ViSession session, ViConstString streamName, ViInt64 nbrElementsToFetch, FetchBuffer & buffer, ViInt64 & remainingElements);

//! Same as #FetchElements without returning the number of remaining elements.
ArraySegment<int32_t> FetchElements(ViSession session, ViConstString streamName, ViInt64 nbrElementsToFetch, FetchBuffer & buffer)
{ ViInt64 nbrRemainingElements = 0; return FetchElements(session, streamName, nbrElementsToFetch, buffer, nbrRemainingElements); }

//! Print observation window samples associated with #recordindex into the #output stream.
/*! The size of the given sample array segment (#sampleArraySegment) must be equal to #nbrObservationWindowElements.
     \param[in] sampleArraySegment: array segment containing samples.
     \param[in] recordIndex: the index of the record samples are associated with.
     \param[in] output: the output stream to write samples to.*/
void PrintObservationWindowSamples(ArraySegment<int32_t>&sampleArraySegment, int64_t recordIndex, std::ostream & output);

//! Decode markers from #peaksArraySegment and print them into the #output stream.
void PrintMarkers(ArraySegment<int32_t>& peaksArraySegment, std::ostream& output);

// name-space gathering all user-configurable parameters
namespace
{
    // Edit resource and options as needed. Resource is ignored if option has Simulate=true.
    // An input signal is necessary if the example is run in non simulated mode, otherwise
    // the acquisition will time out.
    ViChar resource[] = "PXI40::0::0::INSTR";
    ViChar options[]  = "Simulate=true, DriverSetup= Model=SA248P";

    // Acquisition configuration parameters
    ViReal64 const sampleRate = 8.0e9;
    ViReal64 const sampleInterval = 1.0 / sampleRate;
    ViInt64 const recordSize = 16*1024;
    ViInt64 const nbrRecords = 100;
    ViInt32 const streamingMode = AQMD3_VAL_STREAMING_MODE_TRIGGERED_COUNT;
    ViInt32 const acquisitionMode = AQMD3_VAL_ACQUISITION_MODE_PEAK_LIST;

    // Channel configuration parameters
    ViReal64 const range = 1.0;
    ViReal64 const offset = 0.0;
    ViInt32 const coupling = AQMD3_VAL_VERTICAL_COUPLING_DC;

    // Pulse Analysis parameters
    ViInt32 const pklValueSmoothingLength = 3;
    ViInt32 const pklDerivativeSmoothingLength = 7;
    ViInt32 const pklPulseValueThreshold = 512;
    ViInt32 const pklPulseDerivativeThresholdRising = 256;
    ViInt32 const pklPulseDerivativeThresholdFalling = -256;
    ViInt32 const pklPulseDerivativeHysteresis = 16;
    ViInt32 const pklBaseline = 0;

    // Observation Window parameters
    ViBoolean const pklOwEnabled = VI_FALSE;
    ViInt64 const pklOwDelay = 1024;
    ViInt64 const pklOwWidth = 2048;
    ViInt64 const nbrObservationWindowElements = pklOwWidth / 2; // elements are 32-bit, samples are 16-bit.

    // Baseline Correction parameters
    ViInt32 const blMode = AQMD3_VAL_BASELINE_CORRECTION_MODE_DISABLED;
    ViInt32 const blDigitalOffset = 0;
    ViInt32 const blPulseThreshold = 0;
    ViInt32 const blPulsePolarity = AQMD3_VAL_BASELINE_CORRECTION_PULSE_POLARITY_POSITIVE;

    // Trigger configuration parameters
    ViConstString triggerSource = "Internal1";
    ViReal64 const triggerLevel = 0.0;
    ViInt32 const triggerSlope = AQMD3_VAL_TRIGGER_SLOPE_POSITIVE;
    ViReal64 const triggerDelay = 0.0;

    // Fetch parameters
    ViConstString peakStreamName = "PeaksCh1";
    ViConstString sampleStreamName = "StreamCh1";

    /* Number of 32-bit elements to fetch at once.
       NOTE: Please tune according to your system input. Knowing that:
        - 1 trigger generates 8 elements.
        - 1 peak generates 8 elements.*/
    ViInt64 const nbrOfElementsToFetchAtOnce = 1024*1024;

    /*wait-time before a new attempt of read operation. 
      NOTE: Please tune according to your system input (trigger rate & number of peaks)*/
    auto const dataWaitTime = milliseconds(200);

    // Output files
    std::string const peakOutputFileName("TriggeredCountPeakList_peaks.log");
    std::string const dataOutputFileName("TriggeredCountPeakList_data.log");
}

int main()
{
    cout << "Triggered Streaming PeakList \n\n";

    // Initialize the driver. See driver help topic "Initializing the IVI-C Driver" for additional information.
    ViSession session = VI_NULL;
    ViBoolean const idQuery = VI_FALSE;
    ViBoolean const reset   = VI_FALSE;

    try
    {
        checkApiCall( AqMD3_InitWithOptions( resource, idQuery, reset, options, &session ) );

        cout << "\nDriver session initialized\n";

        // Read and output a few attributes.
        ViChar str[128];
        checkApiCall( AqMD3_GetAttributeViString( session, "", AQMD3_ATTR_SPECIFIC_DRIVER_PREFIX,               sizeof( str ), str ) );
        cout << "Driver prefix:      " << str << '\n';
        checkApiCall( AqMD3_GetAttributeViString( session, "", AQMD3_ATTR_SPECIFIC_DRIVER_REVISION,             sizeof( str ), str ) );
        cout << "Driver revision:    " << str << '\n';
        checkApiCall( AqMD3_GetAttributeViString( session, "", AQMD3_ATTR_SPECIFIC_DRIVER_VENDOR,               sizeof( str ), str ) );
        cout << "Driver vendor:      " << str << '\n';
        checkApiCall( AqMD3_GetAttributeViString( session, "", AQMD3_ATTR_SPECIFIC_DRIVER_DESCRIPTION,          sizeof( str ), str ) );
        cout << "Driver description: " << str << '\n';
        checkApiCall( AqMD3_GetAttributeViString( session, "", AQMD3_ATTR_INSTRUMENT_MODEL,                     sizeof( str ), str ) );
        cout << "Instrument model:   " << str << '\n';
        checkApiCall( AqMD3_GetAttributeViString( session, "", AQMD3_ATTR_INSTRUMENT_INFO_OPTIONS,              sizeof( str ), str ) );
        cout << "Instrument options: " << str << '\n';
        checkApiCall( AqMD3_GetAttributeViString( session, "", AQMD3_ATTR_INSTRUMENT_FIRMWARE_REVISION,         sizeof( str ), str ) );
        cout << "Firmware revision:  " << str << '\n';
        checkApiCall( AqMD3_GetAttributeViString( session, "", AQMD3_ATTR_INSTRUMENT_INFO_SERIAL_NUMBER_STRING, sizeof( str ), str ) );
        cout << "Serial number:      " << str << '\n';
        cout << '\n';

        // Abort execution if instrument is still in simulated mode.
        ViBoolean simulate;
        checkApiCall( AqMD3_GetAttributeViBoolean( session, "", AQMD3_ATTR_SIMULATE, &simulate ) );
        if( simulate==VI_TRUE )
        {
            cout << "\nThe Streaming features are not supported in simulated mode.\n";
            cout << "Please update the resource string (resource[]) to match your configuration,";
            cout << " and update the init options string (options[]) to disable simulation.\n";

            AqMD3_close( session );

            return 1;
        }

        // Configure the acquisition in triggered mode with ZeroSuppress enabled.
        cout << "Configuring Acquisition\n";
        cout << "  Number of Records:   " << nbrRecords << '\n';
        cout << "  Record size :        " << recordSize << '\n';
        cout << "  SampleRate:          " << sampleRate << '\n';
        cout << "  Acquisition mode:    " << acquisitionMode << '\n';
        cout << "  Streaming mode :     " << streamingMode << '\n';
        checkApiCall(AqMD3_SetAttributeViInt64(session, "", AQMD3_ATTR_NUM_RECORDS_TO_ACQUIRE, nbrRecords));
        checkApiCall(AqMD3_SetAttributeViInt64(session, "", AQMD3_ATTR_RECORD_SIZE, recordSize));
        checkApiCall( AqMD3_SetAttributeViReal64( session, "", AQMD3_ATTR_SAMPLE_RATE, sampleRate ) );
        checkApiCall( AqMD3_SetAttributeViInt32( session, "", AQMD3_ATTR_ACQUISITION_MODE, acquisitionMode) );
        checkApiCall(AqMD3_SetAttributeViInt32(session, "", AQMD3_ATTR_STREAMING_MODE, streamingMode));

        // Configure the channels.
        cout << "Configuring Channel1\n";
        cout << "  Range:              " << range << '\n';
        cout << "  Offset:             " << offset << '\n';
        cout << "  Coupling:           " << ( coupling?"DC":"AC" ) << '\n';
        checkApiCall( AqMD3_ConfigureChannel( session, "Channel1", range, offset, coupling, VI_TRUE ) );

        cout << "Configuring Baseline Correction\n";
        cout << "  Mode:               " << blMode << '\n';
        cout << "  Digital Offset:     " << blDigitalOffset << '\n';
        cout << "  Pulse Threshold:    " << blPulseThreshold << '\n';
        cout << "  Pulse Polarity:     " << blPulsePolarity << '\n';
        checkApiCall(AqMD3_SetAttributeViInt32(session, "Channel1", AQMD3_ATTR_CHANNEL_BASELINE_CORRECTION_MODE, blMode));
        checkApiCall(AqMD3_SetAttributeViInt32(session, "Channel1", AQMD3_ATTR_CHANNEL_BASELINE_CORRECTION_DIGITAL_OFFSET, blDigitalOffset));
        checkApiCall(AqMD3_SetAttributeViInt32(session, "Channel1", AQMD3_ATTR_CHANNEL_BASELINE_CORRECTION_PULSE_THRESHOLD, blPulseThreshold));
        checkApiCall(AqMD3_SetAttributeViInt32(session, "Channel1", AQMD3_ATTR_CHANNEL_BASELINE_CORRECTION_PULSE_POLARITY, blPulsePolarity));

        cout << "Configuring PeakList Pulse Analysis\n";
        cout << "  Value smoothing length:             " << pklValueSmoothingLength << '\n';
        cout << "  Derivative smoothing length:        " << pklDerivativeSmoothingLength << '\n';
        cout << "  Pulse value threshold:              " << pklPulseValueThreshold << '\n';
        cout << "  Pulse derivative threshold rising:  " << pklPulseDerivativeThresholdRising << '\n';
        cout << "  Pulse derivative threshold falling: " << pklPulseDerivativeThresholdFalling << '\n';
        cout << "  Pulse derivative hysteresis:        " << pklPulseDerivativeHysteresis << '\n';
        cout << "  Baseline:                           " << pklBaseline << '\n';
        checkApiCall(AqMD3_SetAttributeViInt32(session, "Channel1", AQMD3_ATTR_CHANNEL_PEAK_LIST_VALUE_SMOOTHING_LENGTH, pklValueSmoothingLength));
        checkApiCall(AqMD3_SetAttributeViInt32(session, "Channel1", AQMD3_ATTR_CHANNEL_PEAK_LIST_DERIVATIVE_SMOOTHING_LENGTH, pklDerivativeSmoothingLength));
        checkApiCall(AqMD3_SetAttributeViInt32(session, "Channel1", AQMD3_ATTR_CHANNEL_PEAK_LIST_PULSE_VALUE_THRESHOLD, pklPulseValueThreshold));
        checkApiCall(AqMD3_SetAttributeViInt32(session, "Channel1", AQMD3_ATTR_CHANNEL_PEAK_LIST_PULSE_DERIVATIVE_THRESHOLD_RISING, pklPulseDerivativeThresholdRising));
        checkApiCall(AqMD3_SetAttributeViInt32(session, "Channel1", AQMD3_ATTR_CHANNEL_PEAK_LIST_PULSE_DERIVATIVE_THRESHOLD_FALLING, pklPulseDerivativeThresholdFalling));
        checkApiCall(AqMD3_SetAttributeViInt32(session, "Channel1", AQMD3_ATTR_CHANNEL_PEAK_LIST_PULSE_DERIVATIVE_HYSTERESIS, pklPulseDerivativeHysteresis));
        checkApiCall(AqMD3_SetAttributeViInt32(session, "Channel1", AQMD3_ATTR_CHANNEL_PEAK_LIST_BASELINE, pklBaseline));

        if (pklOwEnabled != VI_FALSE)
        {
            cout << "Configuring PeakList Observation Window\n";
            cout << "  Enabled:     " << pklOwEnabled << '\n';
            cout << "  Delay:       " << pklOwDelay << '\n';
            cout << "  Width:       " << pklOwWidth << '\n';
            checkApiCall(AqMD3_SetAttributeViBoolean(session, "Channel1", AQMD3_ATTR_CHANNEL_PEAK_LIST_OBSERVATION_WINDOW_ENABLED, pklOwEnabled));
            checkApiCall(AqMD3_SetAttributeViInt64(session, "Channel1", AQMD3_ATTR_CHANNEL_PEAK_LIST_OBSERVATION_WINDOW_DELAY, pklOwDelay));
            checkApiCall(AqMD3_SetAttributeViInt64(session, "Channel1", AQMD3_ATTR_CHANNEL_PEAK_LIST_OBSERVATION_WINDOW_WIDTH, pklOwWidth));
        }

        // Configure the trigger.
        cout << "Configuring Trigger\n";
        cout << "  ActiveSource:       " << triggerSource << '\n';
        cout << "  Level:              " << triggerLevel << "\n";
        cout << "  Slope:              " << (triggerSlope ? "Positive" : "Negative") << "\n";
        checkApiCall( AqMD3_SetAttributeViString( session, "", AQMD3_ATTR_ACTIVE_TRIGGER_SOURCE, triggerSource ) );
        checkApiCall( AqMD3_SetAttributeViReal64( session, triggerSource, AQMD3_ATTR_TRIGGER_LEVEL, triggerLevel ) );
        checkApiCall( AqMD3_SetAttributeViInt32( session, triggerSource, AQMD3_ATTR_TRIGGER_SLOPE, triggerSlope ) );

        // Calibrate the instrument.
        cout << "\nApply setup and run self-calibration\n";
        checkApiCall( AqMD3_ApplySetup( session ) );
        AqMD3_SelfCalibrate( session );

        // Prepare readout buffer
        ViInt64 peakStreamGrain = 0;
        checkApiCall( AqMD3_GetAttributeViInt64( session, peakStreamName , AQMD3_ATTR_STREAM_GRANULARITY_IN_BYTES, &peakStreamGrain) );

        ViInt64 sampleStreamGrain = 0;
        checkApiCall( AqMD3_GetAttributeViInt64(session, sampleStreamName, AQMD3_ATTR_STREAM_GRANULARITY_IN_BYTES, &sampleStreamGrain) );

        ViInt64 const peaksBufferSize = nbrOfElementsToFetchAtOnce              // required elements
                                       + peakStreamGrain / sizeof(ViInt32) - 1; // alignment overhead

        ViInt64 const sampleBufferSize = nbrObservationWindowElements             // required elements
                                       + sampleStreamGrain / sizeof(ViInt32) - 1; // alignment overhead


        FetchBuffer peaksBuffer (static_cast<size_t>(peaksBufferSize));
        FetchBuffer sampleBuffer(static_cast<size_t>(sampleBufferSize));

        // Count the total volume of fetched markers and elements.
        ViInt64 totalMarkerElements = 0;
        ViInt64 totalSampleElements = 0;

        // open output file (used to save record descriptors in human friendly format).
        std::ofstream peakOutputFile(peakOutputFileName);
        std::ofstream dataOutputFile;

        if (pklOwEnabled)
            dataOutputFile.open(dataOutputFileName);

        int64_t recordIndex = 0;
        // Start the acquisition.
        cout << "\nInitiating acquisition\n";
        checkApiCall( AqMD3_InitiateAcquisition( session ) );
        cout << "Acquisition is running\n\n";


        // Wait for acquisition to complete, fetch marker data simlutaneously.
        ViInt32 isIdle = AQMD3_VAL_ACQUISITION_STATUS_RESULT_FALSE;
        for(;isIdle != AQMD3_VAL_ACQUISITION_STATUS_RESULT_TRUE;)
        {
            ViInt64 remainingPeakElements=0;
            ArraySegment<int32_t> peaksArraySegment = FetchAvailableElements(session, peakStreamName, nbrOfElementsToFetchAtOnce, peaksBuffer, remainingPeakElements);
            totalMarkerElements += peaksArraySegment.Size();

            if (peaksArraySegment.Size() > 0)
            {
                cout << "Fetched " << peaksArraySegment.Size() << " elements from " << peakStreamName << " stream. Remaining elements: " << remainingPeakElements << "\n";
                PrintMarkers(peaksArraySegment, peakOutputFile);
            }
            else
            {
                std::cout << "wait for data\n";
                sleep_for(dataWaitTime);
            }

            // fetch observation window samples
            if (pklOwEnabled != VI_FALSE)
            {
                ViInt64 remainingSampleElements = 0;
                ArraySegment<int32_t> sampleArraySegment = FetchElements(session, sampleStreamName, nbrObservationWindowElements, sampleBuffer, remainingSampleElements);
                totalSampleElements += sampleArraySegment.Size();

                if (sampleArraySegment.Size() != 0)
                    PrintObservationWindowSamples(sampleArraySegment, recordIndex++, dataOutputFile);

                while(remainingSampleElements >= nbrObservationWindowElements)
                {
                    ArraySegment<int32_t> sampleArraySegment = FetchElements(session, sampleStreamName, nbrObservationWindowElements, sampleBuffer);
                    totalSampleElements += sampleArraySegment.Size();
                    remainingSampleElements -= sampleArraySegment.Size();

                    PrintObservationWindowSamples(sampleArraySegment, recordIndex++, dataOutputFile);
                }
            }

            checkApiCall(AqMD3_IsIdle(session, &isIdle));
        }

        // acquisition is complete, read remaining markers
        for (;;)
        {
            ViInt64 remainingPeakElements = 0;
            ArraySegment<int32_t> peaksArraySegment = FetchAvailableElements(session, peakStreamName, nbrOfElementsToFetchAtOnce, peaksBuffer, remainingPeakElements);
            totalMarkerElements += peaksArraySegment.Size();

            if (peaksArraySegment.Size() > 0)
            {
                cout << "Fetched " << peaksArraySegment.Size() << " elements from " << peakStreamName << " stream. Remaining elements: " << remainingPeakElements << "\n";
                PrintMarkers(peaksArraySegment, peakOutputFile);
            }
            else
            {
                if(remainingPeakElements != 0)
                    throw std::logic_error("Fetch returned empty buffer while instrument indicated "+ToString(remainingPeakElements) + " remaining elements");

                std::cout<< "No additional markers\n";
                break;
            }
        }

        // read remaining samples
        if (pklOwEnabled != VI_FALSE)
        {
            ViInt64 remainingSampleElements = 0;
            for (;;)
            {
                ArraySegment<int32_t> sampleArraySegment = FetchElements(session, sampleStreamName, nbrObservationWindowElements, sampleBuffer, remainingSampleElements);
                totalSampleElements += sampleArraySegment.Size();
                if (sampleArraySegment.Size() != 0)
                {
                    PrintObservationWindowSamples(sampleArraySegment, recordIndex, dataOutputFile);
                    ++recordIndex;
                }
                else
                {
                    std::cout << "No additional samples\n";
                    break;
                }
            }
        }

        if (dataOutputFile.is_open())
            dataOutputFile.close();

        peakOutputFile.close();

        ViInt64 const totalData = (totalMarkerElements + totalSampleElements) * sizeof(ViInt32);
        cout << "Total data read: " << (totalData /(1024*1024)) << " MBytes.\n";

        // Close the session.
        checkApiCall( AqMD3_close( session ) );
        cout << "\nDriver session closed\n";
        return 0;
    }
    catch (std::exception const& exc)
    {
        std::cerr << "Unexpected error: " << exc.what() << std::endl;

        if (session != VI_NULL)
        {
            // Abort any ongoing acquisition
            ViInt32 acqStatus = AQMD3_VAL_ACQUISITION_STATUS_RESULT_TRUE;
            if ( VI_SUCCESS != AqMD3_IsIdle( session, &acqStatus ) )
                cerr << "Failed to read acquisition status\n";
            else if (acqStatus != AQMD3_VAL_ACQUISITION_STATUS_RESULT_TRUE)
            {
                if ( VI_SUCCESS != AqMD3_Abort( session ) )
                    cerr << "Failed to abort the acquisition\n";
            }

            // close the instrument
            if ( VI_SUCCESS != AqMD3_close( session ) )
                cerr << "Failed to close the instrument\n";
        }

        cout << "\nException handling complete.\n";

        return(1);
    }
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////
//
// Definition of local functions
//

// Utility function to check status error during driver API call.
void testApiCall( ViStatus status, char const * functionName )
{
    ViInt32 ErrorCode;
    ViChar ErrorMessage[512];

    if( status>0 ) // Warning occurred.
    {
        AqMD3_GetError( VI_NULL, &ErrorCode, sizeof( ErrorMessage ), ErrorMessage );
        cerr << "** Warning during " << functionName << ": 0x" << hex << ErrorCode << ", " << ErrorMessage << '\n';
    }
    else if( status<0 ) // Error occurred.
    {
        AqMD3_GetError( VI_NULL, &ErrorCode, sizeof( ErrorMessage ), ErrorMessage );
        cerr << "** ERROR during " << functionName << ": 0x" << hex << ErrorCode << ", " << ErrorMessage << '\n';
        throw std::runtime_error( ErrorMessage );
    }
}

ArraySegment<int32_t> FetchAvailableElements(ViSession session, ViConstString streamName, ViInt64 nbrElementsToFetch, FetchBuffer& buffer, ViInt64& remainingElements)
{
    int32_t* bufferData = buffer.data();
    ViInt64 const bufferSize = buffer.size();

    if (bufferSize < nbrElementsToFetch)
        throw std::invalid_argument("Buffer size is smaller than the requested elements to fetch");

    ViInt64 firstValidElement = 0;
    ViInt64 actualElements = 0;
    remainingElements = 0;

    // Try to fetch the requested volume of elements.
    checkApiCall(AqMD3_StreamFetchDataInt32(session, streamName, nbrElementsToFetch, bufferSize, (ViInt32*)bufferData, &remainingElements, &actualElements, &firstValidElement));

    if ((actualElements == 0) && (remainingElements > 0))
    {
        /* Fetch failed to read data because the number of available elements is smaller than the requested volume.*/

        // Check that the number of available elements is smaller than requested
        if (nbrElementsToFetch <= remainingElements)
            throw std::logic_error("First fetch failed to read " + ToString(nbrElementsToFetch) + " elements when it reports " + ToString(remainingElements) + " available elements.");

        // Read available elements
        checkApiCall(AqMD3_StreamFetchDataInt32(session, streamName, remainingElements, bufferSize, (ViInt32*)bufferData, &remainingElements, &actualElements, &firstValidElement));
    }

    // this buffer might be empty if fetch failed to read actual data.
    return ArraySegment<int32_t>(buffer, (size_t)firstValidElement, (size_t)actualElements);
}

ArraySegment<int32_t> FetchElements(ViSession session, ViConstString streamName, ViInt64 nbrElementsToFetch, FetchBuffer& buffer, ViInt64& remainingElements)
{
    int32_t* bufferData = buffer.data();
    ViInt64 const bufferSize = buffer.size();

    if (bufferSize < nbrElementsToFetch)
        throw std::invalid_argument("Buffer size is smaller than the requested elements to fetch");

    ViInt64 firstValidElement = 0;
    ViInt64 actualElements = 0;
    remainingElements = 0;

    // Try to fetch the requested volume of elements.
    checkApiCall(AqMD3_StreamFetchDataInt32(session, streamName, nbrElementsToFetch, bufferSize, (ViInt32*)bufferData, &remainingElements, &actualElements, &firstValidElement));

    // this buffer might be empty if fetch failed to read actual data.
    return ArraySegment<int32_t>(buffer, (size_t)firstValidElement, (size_t)actualElements);
}

//! Expect a trigger marker on #stream, decode it and print its content into the #output stream.
void PrintTriggerMarker(ArraySegment<int32_t>& stream, std::ostream& output)
{
    uint32_t const header = stream[0];
    int const tag = header & 0xff;

    if (tag != 0x11)
        throw std::runtime_error("Expected trigger marker tag, got " + ToString(tag));

    uint32_t const low = stream[1];
    uint32_t const high = stream[2];

    uint32_t recordIndex = (header >> 8) & 0x00ffffff;

    double triggerSubsamplePosition = -(double(low & 0x000000ff) / 256.0);
    uint64_t const trigSampleLow = (low >> 8) & 0x0000000000ffffffL;
    uint64_t const trigSampleHigh = uint64_t(high) << 24;
    uint64_t triggerSampleIndex = trigSampleHigh | trigSampleLow;

    output << "\nTrigger marker: record #" <<  recordIndex << ", trigger sample index = " << triggerSampleIndex << ", subsample = " << triggerSubsamplePosition;
}

//! Expect a pulse marker on #stream, decode it and print its content into the #output stream.
void PrintPulseMarker(ArraySegment<int32_t>& stream, std::ostream& output)
{
    int32_t const header = stream[0];
    int const tag = header & 0xff;

    if (tag != 0x14)
        throw std::runtime_error("Expected pulse marker tag, got " + ToString(tag));

    //1. record index
    uint32_t const recordIndex = (uint32_t)((header >> 8) & 0x00ffffff);

    //2. timestamp
    int32_t const item1 = stream[1];
    int32_t const item2 = stream[2];

    int64_t const tsLow = int64_t(item1) & 0x00000000ffffffffL;
    int64_t const tsHigh = int64_t(item2) & 0x000000000000ffffL;
    int64_t const unsignedTimestamp = tsLow | (tsHigh << 32);

    // timestamp is signed and might be negative when pulse is detected before the trigger
    int64_t const timestamp = ExpandSign(unsignedTimestamp, 48);

    // 3. width
    int32_t const width = (item2 >> 16) & 0x00007fff;

    //4. overflow
    bool const overflow = (((item2 >> 31) & 0x01) != 0);

    //5. number of overrange samples
    int32_t const item3 = stream[3];
    int32_t const nbrOverrangeSamples = item3 & 0x00007fff;

    //6. sum of squares
    int64_t const soSLow = (int64_t(item3) >> 16) & 0x000000000000ffffL;
    int32_t const item4 = stream[4];
    int64_t const soSHigh = int64_t(item4) & 0x00000000ffffffffL;

    int64_t const sumOfSquares = (soSHigh << 16) | soSLow;

    //7. peak position
    int32_t const item5 = stream[5];
    int32_t const item6 = stream[6];

    int const peakXRaw = item5 & 0x00ffffff;
    int const peakYRaw = ((item5 >> 24) & 0x000000ff) | ((item6 & 0x0000ffff) << 8);

    //8. center of mass position
    int32_t const item7 = stream[7];
    int const comXRaw = ((item6 >> 16) & (0x0000ffff)) | ((item7 & 0x000000ff) << 16);
    int const comYRaw = (item7 >> 8) & 0x00ffffff;

    /*9. Scale fixed-point representations (peak & center of mass coordinates) into float-point values.
         Please refer to the User Manual (section "Real-time peak-listing mode (PKL option)") for more
         details on the layout of fixed-point representations associated with the following fields:
         Peak timestamp, peak value, center-of-mass timestamp and center-of-mass value.*/
    static int const peakX_nbrIntegerBits = 14;
    static int const peakX_nbrFractionalBits = 8;
    static int const peakY_nbrIntegerBits = 17;
    static int const peakY_nbrFractionalBits = 3;

    static int const comX_nbrIntegerBits = 16;
    static int const comX_nbrFractionalBits = 8;
    static int const comY_nbrIntegerBits = 16;
    static int const comY_nbrFractionalBits = 1;

    double const peakX = ScaleSigned(peakXRaw, peakX_nbrIntegerBits, peakX_nbrFractionalBits);
    double const peakY = ScaleSigned(peakYRaw, peakY_nbrIntegerBits, peakY_nbrFractionalBits);

    double const comX = ScaleSigned(comXRaw, comX_nbrIntegerBits, comX_nbrFractionalBits);
    double const comY = ScaleSigned(comYRaw, comY_nbrIntegerBits, comY_nbrFractionalBits);

    // print the content of the marker into output stream.
    output << "\n     - Pulse descriptor:";
    output << "\n            - Record index                                      : " << recordIndex;
    output << "\n            - Timestamp (rel. to record's 1st sample)           : " << timestamp;
    if (timestamp < 0)
        output << " (the pulse starts before the trigger)";
    output << "\n            - Width (in samples)                                : " << width;
    output << "\n            - Overrange samples                                 : " << nbrOverrangeSamples;

    output << "\n            - Peak timestamp (rel. to the 1st pulse sample)     : " << peakX;
    output << "\n            - Peak value (16-bit ADC code)                      : " << peakY;

    output << "\n            - Sum of Squares (rel. to baseline, ADC code^2)     : " << sumOfSquares;

    output << "\n            - Center of mass (rel. to the 1st pulse sample)     : " << comX;
    output << "\n            - Center of mass value (rel. to baseline, ADC code) : " << comY;

}

void PrintMarkers(ArraySegment<int32_t>& peaksArraySegment, std::ostream& output)
{
    while (peaksArraySegment.Size() > 0)
    {
        int const tag = peaksArraySegment[0] & 0xff;

        switch (tag)
        {
            case 0x11: // trigger marker
                PrintTriggerMarker(peaksArraySegment, output);
                peaksArraySegment.PopFront(8);
                break;
            case 0x14: // pulse marker
                PrintPulseMarker(peaksArraySegment, output);
                peaksArraySegment.PopFront(8);
                break;
            case 0x1f: // alignment marker
                output << "\n     - alignment marker.";
                peaksArraySegment.PopFront(8);
                break;
            default:
                throw std::runtime_error("Unexpected tag " + ToString(int(tag)));
        }
    }
}

void PrintObservationWindowSamples(ArraySegment<int32_t>& sampleArraySegment, int64_t recordIndex, std::ostream& output)
{
    if (sampleArraySegment.Size() != nbrObservationWindowElements)
        throw std::invalid_argument("Unexpected number of observation window elements. Got=" + ToString(sampleArraySegment.Size()) + ", expected=" + ToString(nbrObservationWindowElements));

    int32_t const* const sampleElements = sampleArraySegment.GetData();
    int16_t const* const samples = reinterpret_cast<int16_t const* const>(sampleElements);
    size_t const nbrSamples = sampleArraySegment.Size() * 2;

    int16_t const* const first = samples;
    int16_t const* const onePastLast = samples + nbrSamples;
    output << "\nRecord #" << recordIndex << " - Observation Window Samples (" << nbrSamples << ") = [";

    for (int16_t const* p = first; p != onePastLast; ++p)
    {
        output << *p << " ";
    }

    output << "]";
}

