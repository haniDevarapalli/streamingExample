            ***********************
            **** Read Me First ****
            ***********************

Version 3.9.31444.53    Oct 2024

Introducing the AqMD3 IVI Driver for the Acqiris Signal Acquisition Components
------------------------------------------------------------------------------
  This instrument driver provides access to the Acqiris Signal Acquisition
  Components through a .NET API. It complies with the IVI specifications
  and offers an IviDigitizer class interface. This driver works in any
  development environment which supports .NET programming including Microsoft
  Visual C++/CLI, Microsoft.NET and others.

Supported Instruments
---------------------
U5303A, U5309A, U5310A, SA220P, SA220E, SA248P, SA248E, SA240P, SA240E, SA230P,
SA230E, U5309E, SA107P, SA107E


Installation
-------------
  System Requirements: The driver installation will check for the following
  requirements. If not found, the installer will either abort, warn, or
  install the required component as appropriate.

  Supported Operating Systems:
    Windows 8.1  (32-bit and 64-bit)
    Windows 10   (32-bit and 64-bit)

  Shared Components
    Before this driver can be installed, your computer must already
    have the IVI Shared Components and IVI.NET Shared Components.

    Minimal version for IVI Shared Components:     5.8.0
    Minimal version for IVI.NET Shared Components: 1.4.0

    The IVI Shared Components installers are available from:
      http://www.ivifoundation.org/shared_components/Default.aspx

  Keysight IO Libraries Suite:
    For the models: U5303A, U5309A and U5310A, your computer must already
    have the Keysight IO Libraries Suite installed.

    Minimal version: 18.1.23218.2

    You can download the latest version from:
      http://www.keysight.com/find/iosuite



Uninstall
---------
  This driver can be uninstalled like any other software from the
  Control Panel using "Programs and Features" in Windows.

  The IVI Shared Components may also be uninstalled like any other
  software from the Control Panel using "Program & Features" in
  Windows.

  Note: The IVI.NET driver requires the IVI Shared Components and IVI.NET
  Shared Components to function. To completely remove IVI components from
  your computer, uninstall all drivers and then uninstall the IVI Shared
  Components and IVI.NET Shared Components.



Start Menu Help File
--------------------
  A shortcut to the driver help file is added to the Start Menu, All Programs,
  Acqiris, MD3, Documentation, AqMD3 IVI.NET Driver. It contains "Getting
  Started" information on using the driver in a variety of programming
  environments as well as documentation on IVI and instrument specific methods
  and properties.

  You will also see shortcuts to the Readme file and many programming
  examples for this driver.


Additional Setup
----------------
  .NET Framework
  The .NET Framework itself is not required by this driver. If you plan to use
  the driver with .NET, the minimal .NET framework version is: 4.0

  The .NET Framework requires an assembly for a .NET driver server. A Primary
  Assembly, along with an XML file for IntelliSense is installed with 
  the driver. The driver's assembly along with IVI assemblies are installed
  by default in:
    <drive>:\<Program Files>\IVI Foundation\IVI\Microsoft.NET\

  The assembly is also installed into the Global Assembly Cache (GAC).



Revision History
----------------
  Version     Date         Notes
  -------   ------------   -----
  3.9.31444.53  Oct 2024  SA107 Release
  3.4.2029.5  Jun 2019  SA230/SA240 Release
  3.2.5562.9  Dec 2018  SA220P Release
  3.1.8850.4  Aug 2018  Initial public release

Note:   For the detailed list of issues and defects corrected with each
        release, please refer to the Release Notes. The Release Notes text file
        is installed with the Acqiris MD3 Software package.


IVI Compliance
--------------
IVI.NET IviDigitizer Specific Instrument Driver
IVI Generation: IVI-2017

IVI Instrument Class: IviDigitizer  Spec: IVI-4.15_v2.3 (IVI.NET)
Group Capabilities Supported:
    IviDigitizerBase                        yes
    IviDigitizerMultiRecordAcquisition      yes
    IviDigitizerBoardTemperature            yes
    IviDigitizerChannelFilter               yes
    IviDigitizerChannelTemperature          yes
    IviDigitizerTimeInterleavedChannels     yes
    IviDigitizerDataInterleavedChannels     no
    IviDigitizerReferenceOscillator         yes
    IviDigitizerSampleClock                 yes
    IviDigitizerSampleMode                  no
    IviDigitizerSelfCalibration             yes
    IviDigitizerDownconversion              no
    IviDigitizerArm                         no
    IviDigitizerMultiArm                    no
    IviDigitizerGlitchArm                   no
    IviDigitizerGlitchTrigger               no
    IviDigitizerPretriggerSamples           no
    IviDigitizerRuntArm                     no
    IviDigitizerSoftwareArm                 no
    IviDigitizerTVArm                       no
    IviDigitizerWidthArm                    no
    IviDigitizerWindowArm                   no
    IviDigitizerTriggerSamples              no
    IviDigitizerMultiTrigger                no
    IviDigitizerRuntTrigger                 no
    IviDigitizerSoftwareTrigger             yes
    IviDigitizerTVTrigger                   no
    IviDigitizerWidthTrigger                no
    IviDigitizerWindowTrigger               no
    IviDigitizerTriggerModifier             no
    IviDigitizerTriggerHoldoff              yes

Optional Features:
  Interchangeability Checking     no
  State Caching                   no
  Coercion Recording              no

Driver Identification:
  Vendor:                         Acqiris
  Description:                    IVI.NET driver for Acqiris Signal Acquisition
                                  Components
  Revision:                       3.4
  Component Identifier:           Acqiris.AqMD3

Hardware Information:
  Instrument Manufacturer:        Acqiris
  Supported Instrument Models:    U5303A, U5309A, U5310A, SA220P, SA220E,
                                  SA248P, SA248E, SA240P, SA240E, SA230P,
                                  SA230E, U5309E, SA107P, SA107E
  Supported Bus Interfaces:       PCIe

32-bit Software Information:
  Supported Operating Systems:    Windows 8.1 32-bit, 10 32-bit
  Support Software Required:      VISA
  Source Code Availability:       Source code not included.

64-bit Software Information:
  Supported Operating Systems:    Windows 8.1 64-bit, 10 64-bit
  Support Software Required:      VISA
  Source Code Availability:       Source code not included.


More Information
----------------
  For more information about this driver and other instrument drivers and
  software available from Acqiris visit:
    http://acqiris.com/

  A list of contact information is available from:
    http://acqiris.com/

  Microsoft, Windows, MS Windows, and Windows NT are U.S.
  registered trademarks of Microsoft Corporation.

Copyright (C) Acqiris SA 2017-2024
