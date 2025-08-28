///
/// Acqiris IVI-C Driver Example Program
///
/// Initializes the driver, reads a few Identity interface properties, and performs a
/// streaming acquisition.
///
/// For additional information on programming with IVI drivers in various IDEs, please see
/// http://www.ivifoundation.org/resources/
///
/// The Example requires a real instrument having CST and input signal on "Channel1". It
/// also requires an AVG option to enable Averager acquisition mode.
///

#include "LibTool.h"
#include <filesystem>
using LibTool::ToString;
#include <AqMD3.h>


#include <iomanip>
#include <iostream>
using std::cerr;
using std::hex;
#include <vector>
using std::vector;
#include <stdexcept>
using std::runtime_error;
#include <chrono>
using std::chrono::minutes;
using std::chrono::seconds;
using std::chrono::milliseconds;
using std::chrono::system_clock;
#include <thread>
using std::this_thread::sleep_for;
#include <fstream>
#include <algorithm>
#include <fstream>
#include <queue>
#include <functional>


#define checkApiCall( f ) do { ViStatus s = f; testApiCall( s, #f ); } while( false )

typedef std::vector<int32_t> FetchBuffer;

//! Validate success status of the given functionName.
void testApiCall(ViStatus status, char const* functionName);

//! Fetch all elements available on module for stream streamName.
/*! The resulting array segment might be empty if no data are available on module.
     \param[in] session: session handle associated with the instrument to read from.
     \param[in] streamName: the stream identifier to read from.
     \param[in] maxElementsToFetch: the maximum number of elements to read.
     \param[in] buffer: buffer used by fetch for read operation.
     \return the array-segment delimiting actual valid data returned by the instrument. The segment might be empty if no data has been read.*/
LibTool::ArraySegment<int32_t> FetchAvailableElements(ViSession session, ViConstString streamName, ViInt64 maxElementsToFetch, FetchBuffer& buffer);

//! Perform a fetch of exact 'nbrElementsToFetch' elements into the given fetch "buffer".
/*! This is a wrapper for low-level function #AqMD3_StreamFetchDataInt32 which adds the following:
      - Return an array-segment to delimit the fetched elements in user-provided buffer.
      - Handle the case where the user request to fetch 0 elements from the stream by returning an empty array-segment. This might be useful when
        all the samples of a record are suppressed (i.e. nothing to read from sample stream).
      - Check the consistency of returned
    \param[in] session: session handle associated with the instrument to read from.
    \param[in] streamName: the stream identifier to read from.
    \param[in] nbrElementsToFetch: the number of elements to read.
    \param[in] buffer: buffer used by fetch for read operation.
    \return the array-segment delimiting actual valid data returned by the instrument. The segment might be empty if no data has been read.
    \throw #std::runtime_error when the buffer size is too small for the requested fetch, or the number fetched elements is different than requested.*/
LibTool::ArraySegment<int32_t> FetchElements(ViSession session, ViConstString streamName, ViInt64 nbrElementsToFetch, FetchBuffer& buffer);

//! Save waveform information into output stream
std::string SaveRecord(LibTool::TriggerMarker const& triggerMarker, ViInt64 nbrRecordElements, LibTool::ArraySegment<int32_t> const& elementBuffer, double timestampInterval, std::ostream& output);

//! Return the timestamping period for model (expressed in seconds)
double GetTimestampPeriodForModel(std::string const& model);



// name-space gathering all user-configurable parameters
namespace
{
    // Edit resource and options as needed. Resource is ignored if option has Simulate=true.
    // An input signal is necessary if the example is run in non simulated mode, otherwise
    // the acquisition will time out.
    ViChar resource[] = "PXI5::0::0::INSTR";
    ViChar options[] = "Simulate=false, DriverSetup= Model=SA240P";

    // Acquisition configuration parameters
    bool const channelInterleavingEnabled = false;
    ViReal64 const sampleRate = 2.0e9;
    ViReal64 const sampleInterval = 1.0 / sampleRate;
    ViInt64 const recordSize = 18432;
    ViInt32 const streamingMode = AQMD3_VAL_STREAMING_MODE_TRIGGERED;
    ViInt32 const acquisitionMode = AQMD3_VAL_ACQUISITION_MODE_NORMAL;

    // Channel configuration parameters
    ViReal64 const range = 2;
    ViReal64 const offset = 0.0;
    ViInt32 const coupling = AQMD3_VAL_VERTICAL_COUPLING_DC;

    // Trigger configuration parameters
    ViConstString triggerSource = "External1";
    ViReal64 const triggerLevel = 1;
    ViInt32 const triggerSlope = AQMD3_VAL_TRIGGER_SLOPE_POSITIVE;

    // Fetch parameters

    //Name of the Input Channel, basically naming the stream
    ViConstString sampleStreamName = "StreamCh1";

    //Name of the triggers basically helps us identify a trigger information
    ViConstString markerStreamName = "MarkersCh1";

    //The total number of records to fetch at once
    ViInt64 const maxRecordsToFetchAtOnce = 15;


    int64_t const nbrSamplesPerElement = sizeof(int32_t) / sizeof(int16_t);

    //Calculates how many elements (int32s) are in one record.
    ViInt64 const nbrRecordElements = recordSize / nbrSamplesPerElement;

    //Calculates the maximum number of waveform elements we will fetch in one go.
    ViInt64 const maxAcquisitionElements = nbrRecordElements * maxRecordsToFetchAtOnce;

    ViInt64 const maxMarkerElements = LibTool::StandardStreaming::NbrTriggerMarkerElements * maxRecordsToFetchAtOnce;

    /* Wait-time before a new attempt of read operation.
           NOTE: Please tune according to your system input (trigger rate & number of peaks)*/


    auto const dataWaitTime = milliseconds(100);

    /* Wait for samples to be ready for fetch*/
    //Calculates how long it takes (in milliseconds) to record one record worth of samples.
    //calculates how long it takes to collect 1 record, which has recordSize of the number of samples we asked it collect but not all the records.
    int64_t const recordDurationInMs = std::max(static_cast<int64_t>(recordSize * sampleInterval * 1000.0), int64_t(1));

    //Allows up to 3 attempts to wait for new samples before giving up.
    //If each fetch attempt fails (data not ready), the system will Wait 200 ms (above)
    //Try again (up to 3 times total)
    //This avoids crashing on a temporary hiccup.
    int const nbrWaitForSamplesAttempts = 3;

    // duration of the streaming session
    //Tells the duration of streaming, not quite sure why and how this is 60seconds
    //recordSize and numRecords define one acquisition batch
    //streamingDuration tells your code to keep doing batches repeatedly for that long
    //It allows continuous, real-time streaming instead of just grabbing one batch and stopping
    auto const streamingDuration = minutes(2);

    // Output file
    std::string  outputFile("C:\\Users\\Hani\\OneDrive\\Desktop\\acquirisDataAcquisition\\Streaming.log");
    std::vector<std::string> recordWriteBuffer;



}

int main()
{

    // Initialize the driver. See driver help topic "Initializing the IVI-C Driver" for additional information.
    ViSession session = VI_NULL;
    ViBoolean const idQuery = VI_FALSE;
    ViBoolean const reset = VI_FALSE;

    try
    {
        checkApiCall(AqMD3_InitWithOptions(resource, idQuery, reset, options, &session));
        std::cout << "init options success";

        std::cout << "\nDriver session initialized\n";

        // Read and output a few attributes.
        ViChar str[128];
        checkApiCall(AqMD3_GetAttributeViString(session, "", AQMD3_ATTR_SPECIFIC_DRIVER_PREFIX, sizeof(str), str));
        std::cout << "Driver prefix:      " << str << '\n';
        checkApiCall(AqMD3_GetAttributeViString(session, "", AQMD3_ATTR_SPECIFIC_DRIVER_REVISION, sizeof(str), str));
        std::cout << "Driver revision:    " << str << '\n';
        checkApiCall(AqMD3_GetAttributeViString(session, "", AQMD3_ATTR_SPECIFIC_DRIVER_VENDOR, sizeof(str), str));
        std::cout << "Driver vendor:      " << str << '\n';
        checkApiCall(AqMD3_GetAttributeViString(session, "", AQMD3_ATTR_SPECIFIC_DRIVER_DESCRIPTION, sizeof(str), str));
        std::cout << "Driver description: " << str << '\n';
        checkApiCall(AqMD3_GetAttributeViString(session, "", AQMD3_ATTR_INSTRUMENT_MODEL, sizeof(str), str));
        std::cout << "Instrument model:   " << str << '\n';
        std::string const instrumentModel(str);
        checkApiCall(AqMD3_GetAttributeViString(session, "", AQMD3_ATTR_INSTRUMENT_INFO_OPTIONS, sizeof(str), str));
        std::cout << "Instrument options: " << str << '\n';
        std::string const options(str);
        checkApiCall(AqMD3_GetAttributeViString(session, "", AQMD3_ATTR_INSTRUMENT_FIRMWARE_REVISION, sizeof(str), str));
        std::cout << "Firmware revision:  " << str << '\n';
        checkApiCall(AqMD3_GetAttributeViString(session, "", AQMD3_ATTR_INSTRUMENT_INFO_SERIAL_NUMBER_STRING, sizeof(str), str));
        std::cout << "Serial number:      " << str << '\n';
        std::cout << '\n';

        // Abort execution if instrument is still in simulated mode.
        ViBoolean simulate;
        checkApiCall(AqMD3_GetAttributeViBoolean(session, "", AQMD3_ATTR_SIMULATE, &simulate));
        if (simulate == VI_TRUE)
        {
            std::cout << "\nThe Streaming features are not supported in simulated mode.\n";
            std::cout << "Please update the resource string (resource[]) to match your configuration,";
            std::cout << " and update the init options string (options[]) to disable simulation.\n";

            AqMD3_close(session);

            return 1;
        }

        if (options.find("CST") == std::string::npos)
        {
            std::cout << "The required CST module option is missing from the instrument.\n";

            AqMD3_close(session);

            return 1;
        }

        // Get timestamp period.
        /*!
         * When the digitizer logs when a trigger happened, it doesn’t say:
        "This trigger happened at exactly 12.235804 seconds"
        Instead, it gives you a timestamp in ticks — just an integer count.
        To convert it to actual time, you need to know:
        “How much time does each tick represent?” For SA240P its actually recorded
        That’s what timestampPeriod gives you.
         */
        ViReal64 const timestampPeriod = GetTimestampPeriodForModel(instrumentModel);

        // Configure the acquisition in triggered streaming mode.
        std::cout << "Configuring Acquisition\n";
        std::cout << "  Record size :        " << recordSize << '\n';
        std::cout << "  SampleRate:          " << sampleRate << '\n';
        checkApiCall(AqMD3_SetAttributeViInt32(session, "", AQMD3_ATTR_STREAMING_MODE, streamingMode));
        checkApiCall(AqMD3_SetAttributeViReal64(session, "", AQMD3_ATTR_SAMPLE_RATE, sampleRate));
        checkApiCall(AqMD3_SetAttributeViInt32(session, "", AQMD3_ATTR_ACQUISITION_MODE, acquisitionMode));
        checkApiCall(AqMD3_SetAttributeViInt64(session, "", AQMD3_ATTR_RECORD_SIZE, recordSize));

        // Configure the channels.
        std::cout << "Configuring Channel1\n";
        std::cout << "  Range:              " << range << '\n';
        std::cout << "  Offset:             " << offset << '\n';
        std::cout << "  Coupling:           " << (coupling ? "DC" : "AC") << '\n';
        checkApiCall(AqMD3_ConfigureChannel(session, "Channel1", range, offset, coupling, VI_TRUE));

        // Configure the trigger.
        std::cout << "Configuring Trigger\n";
        std::cout << "  ActiveSource:       " << triggerSource << '\n';
        std::cout << "  Level:              " << triggerLevel << "\n";
        std::cout << "  Slope:              " << (triggerSlope ? "Positive" : "Negative") << "\n";

        checkApiCall(AqMD3_SetAttributeViString(session, "", AQMD3_ATTR_ACTIVE_TRIGGER_SOURCE, triggerSource));
        checkApiCall(AqMD3_SetAttributeViReal64(session, triggerSource, AQMD3_ATTR_TRIGGER_LEVEL, triggerLevel));
        checkApiCall(AqMD3_SetAttributeViInt32(session, triggerSource, AQMD3_ATTR_TRIGGER_SLOPE, triggerSlope));


        // Calibrate the instrument.
        std::cout << "\nApply setup and run self-calibration\n";
        checkApiCall(AqMD3_ApplySetup(session));
        checkApiCall(AqMD3_SelfCalibrate(session));

        // Prepare readout buffer
        //the minimum data chunk size that must fetch from this waveform stream
        ViInt64 sampleStreamGrain = 0;

        //Which record (trigger shot) this data belongs to and Where exactly in the data the trigger happened
        ViInt64 markerStreamGrain = 0;

        //You must fetch at least N KB of waveform data at a time.
        checkApiCall(AqMD3_GetAttributeViInt64(session, sampleStreamName, AQMD3_ATTR_STREAM_GRANULARITY_IN_BYTES, &sampleStreamGrain));

        //You must fetch at least N bytes of marker data at a time
        checkApiCall(AqMD3_GetAttributeViInt64(session, markerStreamName, AQMD3_ATTR_STREAM_GRANULARITY_IN_BYTES, &markerStreamGrain));

        //This converts the grain size from bytes to number of elements. If each sample is int32_t (4 bytes), and the grain is 16,384 bytes:
        //So your data fetches must be in multiples of 4096 samples.
        ViInt64 const sampleStreamGrainElements = sampleStreamGrain / sizeof(int32_t);
        ViInt64 const markerStreamGrainElements = markerStreamGrain / sizeof(int32_t);

        ViInt64 const sampleStreamBufferSize = maxAcquisitionElements        // required elements
            + maxAcquisitionElements / 2      // unfolding overhead (only in single channel mode)
            + sampleStreamGrainElements - 1;// alignment overhead

        ViInt64 const markerStreamBufferSize = maxMarkerElements             // required elements
            + markerStreamGrainElements - 1;// alignment overhead

        FetchBuffer sampleStreamBuffer(static_cast<size_t>(sampleStreamBufferSize));
        FetchBuffer markerStreamBuffer(static_cast<size_t>(markerStreamBufferSize));


        // Expected values and statistics
        //This represents the timestamp of the very first sample in the entire streaming session — usually relative to the start of acquisition.
        double minXtime = 0.0; // InitialXTime is the time of the very first sample in the record.

        //This keeps track of the record index you expect next in the streaming process.
        ViInt64 expectedRecordIndex = 0;

        // Count the total volume of fetched markers and elements.
        //number of int32_t sample points fetched
        ViInt64 totalSampleElements = 0;

        //number of int32_t marker values fetched
        ViInt64 totalMarkerElements = 0;




        // Start the acquisition.
        std::cout << "\nInitiating acquisition\n";
        checkApiCall(AqMD3_InitiateAcquisition(session));
        std::cout << "Acquisition is running\n\n";


        // std::ofstream outputFile(outputFileName);

        //Calculating the total time we want to run the acquisition for
        //Assuming we start at time 12:00 and we set our time duration of 1 min
        //the loop should run till 1 min
        auto const endTime = system_clock::now() + streamingDuration;
        while (system_clock::now() < endTime)
        {
            // Fetch markers of requested records
            LibTool::ArraySegment<int32_t> markerArraySegment = FetchAvailableElements(session, markerStreamName, maxMarkerElements, markerStreamBuffer);
            totalMarkerElements += markerArraySegment.Size();

            // std::cout << "Fetched marker values: " << markerArraySegment.Size() << "\n";


            // If the fetch fails to read data, then wait before a new attempt.
            if (markerArraySegment.Size() == 0)
            {
                std::cout << "waiting for data\n";
                sleep_for(dataWaitTime);
                continue;
            }

            int64_t const numAvailableRecords = int64_t(markerArraySegment.Size() / LibTool::StandardStreaming::NbrTriggerMarkerElements);
            // std::cout << "Number of triggers (records) detected: " << numAvailableRecords << "\n";
            // std::cout << "Expecting to fetch samples: " << numAvailableRecords * nbrRecordElements << "\n";

            /*!
             * markerArraySegment.Size()	Total number of int32_t values fetched
               NbrTriggerMarkerElements = 16	Each trigger marker is made of 16 values
               markerArraySegment.Size() / 16	Total number of complete trigger records  received
             */




             // Fetch all samples of requested records
             // Fetch the samples corresponding to those new records
             // Multiply number of records × record size to know how many samples to fetch
            LibTool::ArraySegment<int32_t> sampleArraySegment = FetchElements(session, sampleStreamName, numAvailableRecords * nbrRecordElements, sampleStreamBuffer);
            totalSampleElements += sampleArraySegment.Size();

            // std::cout << "Fetched waveform elements: " << sampleArraySegment.Size() << "\n";
            // std::cout << "Expected waveform elements: " << numAvailableRecords * nbrRecordElements << "\n";

            if (sampleArraySegment.Size() != numAvailableRecords * nbrRecordElements)
            {
                std::cout << "Mismatch in expected vs fetched waveform data!";
            }


            // Process acquired records
            std::cout << "Num Of Available records = " << numAvailableRecords;
            for (int64_t i = 0; i < numAvailableRecords; ++i)
            {
                // 1. decode trigger marker from marker stream
                LibTool::TriggerMarker const nextTriggerMarker = LibTool::StandardStreaming::DecodeTriggerMarker(markerArraySegment);

                // 2. Validate marker consistency: tag, incrementing record index, increasing xtime.
                if (LibTool::MarkerTag::TriggerNormal != nextTriggerMarker.tag)
                    throw std::runtime_error("Unexpected trigger marker tag: got " + ToString(int(nextTriggerMarker.tag)) + ", expected " + ToString(int(LibTool::MarkerTag::TriggerNormal)));

                // 2.1 Check that the record descriptor holds the expected record index.
                if ((expectedRecordIndex & LibTool::TriggerMarker::RecordIndexMask) != nextTriggerMarker.recordIndex)
                    throw std::runtime_error("Unexpected record index: expected=" + ToString(expectedRecordIndex) + ", got " + ToString(nextTriggerMarker.recordIndex));

                // 2.2 initialXTime (time of first sample in record) must increase.
                ViReal64 const xtime = nextTriggerMarker.GetInitialXTime(timestampPeriod);
                if (xtime <= minXtime)
                    throw std::runtime_error("InitialXTime not increasing: minimum expected=" + ToString(minXtime) + ", got " + ToString(xtime));

                std::vector<float> waveFormData;
                waveFormData.reserve(nbrRecordElements * nbrSamplesPerElement);
                for (int64_t j = 0; j < nbrRecordElements; ++j)
                {
                    int32_t packed = sampleArraySegment[j];
                    waveFormData.push_back(float(int16_t(packed & 0xFFFF)));
                    waveFormData.push_back(float(int16_t((packed >> 16) & 0xFFFF)));
                }

                if (waveFormData.size() != recordSize)
                {
                    std::cout << "Error: Waveform size mismatch with expected recordSize!";
                }

                //now we fetched the current waveforms data and the time it was acquired at


                std::vector<int> sampleData;
                for (int i = 0; i < 5; i++)
                {
                    std::cout << sampleData[i];
                }

                // 3.1 remove record elements from the segment and advance to elements of the next record
                sampleArraySegment.PopFront(nbrRecordElements);

                //Prepares to validate the next record's index
                ++expectedRecordIndex;

                //Updates last known timestamp to check for time ordering in the next record
                minXtime = xtime;
            }
        }


        // outputFile.close();

        ViInt64 const totalSampleData = totalSampleElements * sizeof(ViInt32);
        ViInt64 const totalMarkerData = totalMarkerElements * sizeof(ViInt32);

        std::cout << "Total Marker Elements = " << totalMarkerElements << totalMarkerElements * sizeof(ViInt32);
        std::cout << "\nTotal sample data read: " << (totalSampleData / (1024 * 1024)) << " MBytes.\n";
        std::cout << "Marker Data = " << totalMarkerElements << totalMarkerElements * sizeof(ViInt32);
        std::cout << "Total marker data read: " << (totalMarkerData / (1024 * 1024)) << " MBytes.\n";
        std::cout << "Duration: " << (streamingDuration / seconds(1)) << " seconds.\n";
        ViInt64 const totalData = totalSampleData + totalMarkerData;
        std::cout << "Data rate: " << (totalData) / (1024 * 1024) / (streamingDuration / minutes(2)) << " MB/s.\n";


        // Stop the acquisition.
        std::cout << "\nStopping acquisition\n";
        checkApiCall(AqMD3_Abort(session));

        // Close the session.
        checkApiCall(AqMD3_close(session));
        std::cout << "\nDriver session closed\n";
        return 0;
    }
    catch (std::exception const& exc)
    {
        std::cerr << "Unexpected error: " << exc.what() << std::endl;

        return(1);
    }
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////
//
// Definition of local functions
//

// Utility function to check status error during driver API call.
void testApiCall(ViStatus status, char const* functionName)
{
    ViInt32 ErrorCode;
    ViChar ErrorMessage[512];

    if (status > 0) // Warning occurred.
    {
        AqMD3_GetError(VI_NULL, &ErrorCode, sizeof(ErrorMessage), ErrorMessage);
        std::cout << "** Warning during " << functionName << ": 0x" << hex << ErrorCode << ", " << ErrorMessage << '\n';
    }
    else if (status < 0) // Error occurred.
    {
        AqMD3_GetError(VI_NULL, &ErrorCode, sizeof(ErrorMessage), ErrorMessage);
        std::cout << "** ERROR during " << functionName << ": 0x" << hex << ErrorCode << ", " << ErrorMessage << '\n';
        throw runtime_error(ErrorMessage);
    }
}

LibTool::ArraySegment<int32_t> FetchAvailableElements(ViSession session, ViConstString streamName, ViInt64 nbrElementsToFetch, FetchBuffer& buffer)
{
    int32_t* bufferData = buffer.data();
    ViInt64 const bufferSize = buffer.size();

    if (bufferSize < nbrElementsToFetch)
        throw std::invalid_argument("Buffer size is smaller than the requested elements to fetch");

    ViInt64 firstValidElement = 0;
    ViInt64 actualElements = 0;
    ViInt64 remainingElements = 0;

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

    // if (actualElements > 0)
    // {
    //     std::cout << "Fetched " << actualElements << " elements from " << streamName << " stream. Remaining elements: " << remainingElements << "\n";
    // }

    // this buffer might be empty if fetch failed to read actual data.
    return LibTool::ArraySegment<int32_t>(buffer, (size_t)firstValidElement, (size_t)actualElements);
}

LibTool::ArraySegment<int32_t> FetchElements(ViSession session, ViConstString streamName, ViInt64 nbrElementsToFetch, FetchBuffer& buffer)
{
    // Handle the special case there is no need to fetch elements (this might happen when all samples are suppressed) by returning an empty array segment
    if (nbrElementsToFetch == 0)
        return LibTool::ArraySegment<int32_t>(buffer, 0, 0);

    int32_t* bufferData = buffer.data();
    ViInt64 const bufferSize = buffer.size();

    if (bufferSize < nbrElementsToFetch)
        throw std::invalid_argument("Buffer size is smaller than the requested elements to fetch");

    for (int nbrAttempts = 0; nbrAttempts < nbrWaitForSamplesAttempts; ++nbrAttempts)
    {
        ViInt64 firstElement = 0;
        ViInt64 actualElements = 0;
        ViInt64 remainingElements = 0;

        // Try to fetch the requested volume of elements.
        checkApiCall(AqMD3_StreamFetchDataInt32(session, streamName, nbrElementsToFetch, bufferSize, (ViInt32*)bufferData, &remainingElements, &actualElements, &firstElement));

        if (nbrElementsToFetch == actualElements)
        {
            // Requested volume has been successfully  fetched.
            // std::cout << "Fetched " << actualElements << " elements from " << streamName << " stream. Remaining elements: " << remainingElements << "\n";
            return LibTool::ArraySegment<int32_t>(buffer, size_t(firstElement), size_t(actualElements));
        }
        else
        {
            if ((actualElements == 0) && (remainingElements < nbrElementsToFetch))
            {
                /* Sometimes, the fetch fail because data might not be ready for fetch immediately.
                   Make another attempt after a short wait. */
                std::cout << "Wait for record samples to be ready for fetch\n";
                std::this_thread::sleep_for(std::chrono::milliseconds(recordDurationInMs));
                continue;
            }

            /* The following error might occurs in case of stream overflow error where sample storage in memory is interrupted
               at overflow event. The very last record is incomplete in this case.*/
            throw std::runtime_error("Number of fetched elements is different than requested. Requested=" + ToString(nbrElementsToFetch) + " , fetched=" + ToString(actualElements) + ".");
        }
    }

    throw std::runtime_error("Failed to fetch requested data from " + ToString(streamName) + " after " + ToString(nbrWaitForSamplesAttempts) + " attempts");
}


std::string SaveRecord(LibTool::TriggerMarker const& triggerMarker, ViInt64 nbrRecordElements, LibTool::ArraySegment<int32_t> const& elementBuffer, double timestampInterval, std::ostream& output)
{
    std::ostringstream out;
    out << "TriggerIndex=" << triggerMarker.recordIndex << ", Time=" << triggerMarker.GetInitialXTime(timestampInterval);

    for (int64_t i = 0; i < nbrRecordElements; ++i)
    {
        int32_t packed = elementBuffer[i];
        int16_t s1 = int16_t(packed & 0xFFFF);
        int16_t s2 = int16_t((packed >> 16) & 0xFFFF);
        out << "," << s1 << "," << s2;
    }

    return out.str();


    // double const xTime = triggerMarker.GetInitialXTime(timestampInterval);
    // double const xOffset = triggerMarker.GetInitialXOffset(sampleInterval);
    // output << "# record index                 : " << std::dec << triggerMarker.recordIndex << '\n';
    // output << "# Absolute Time of First Sample: " << std::setprecision(12) << xTime << '\n';
    // output << "# Absolute Time of Trigger     : " << std::setprecision(12) << xTime+xOffset << '\n';

    // size_t const nbrRecordSamples = size_t(nbrRecordElements) * size_t(nbrSamplesPerElement);
    // int16_t* sampleArray = reinterpret_cast<int16_t*>(elementBuffer.GetData());

    // output << "Samples(" << std::dec << nbrRecordSamples << ") = [ ";

    // // Print all samples of small records.
    // if (nbrRecordSamples <= 16)
    // {
    //     for (size_t i = 0; i < nbrRecordSamples; ++i)
    //         output << (int)sampleArray[i] <<" ";
    // }
    // else
    // {
    //     // print first five and last two samples.
    //     output << (int)sampleArray[0] << " "
    //            << (int)sampleArray[1] << " "
    //            << (int)sampleArray[2] << " "
    //            << (int)sampleArray[3] << " "
    //            << (int)sampleArray[4] << " "
    //            << "... "
    //            << (int)sampleArray[nbrRecordSamples-2] << " "
    //            << (int)sampleArray[nbrRecordSamples-1] << " ";
    // }

    // output << "]\n\n";
}

ViReal64 GetTimestampPeriodForModel(std::string const& model)
{
    if (model == "SA220P" || model == "SA220E")
        return 500e-12;
    else if (model == "SA230P" || model == "SA230E")
        return 250e-12;
    else if (model == "SA240P" || model == "SA240E")
        return 250e-12;
    else if (model == "SA217P" || model == "SA217E")
        return 250e-12;
    else if (model == "SA248P" || model == "SA248E")
        return 125e-12;
    else
        throw std::invalid_argument("Cannot deduce timestamp period for instrument: " + model);
}



