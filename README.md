
Requirements
SA240P Digitizer (hardware required to run the streaming example)
Streaming Example project


While running the IVIC Streaming example on the SA240P digitizer, the following error may occasionally occur:
Unexpected response: Power supplies are unstable. 
Required bits 0x0000013f, status=0xffffffff, lost=0xffffffff

This issue has been observed primarily during application startup or immediately launching the software after a crash.

This situation can arise if the application terminates unexpectedly while an acquisition is active, or if a new session is opened before the hardware has fully stabilized from a previous run. 
The condition does not occur consistently but can be replicated by forcing abrupt exits (for example, triggering an out-of-range vector access during streaming and restarting the program).

To test this behavior, I deliberately introduced a vector out-of-range error during streaming to force the application to crash. After doing so, the “power supplies unstable” error did not appear immediately every time, but it did occur occasionally on restart. 
This suggests the issue is intermittent and related to the device state after an abrupt termination, rather than being consistently reproducible on demand.

