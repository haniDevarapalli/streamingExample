//////////////////////////////////////////////////////////////////////////////////////////
//
//  Copyright (C) Acqiris 2014-2018
//----------------------------------------------------------------------------------------
//  Started:    16 January 2014
//  By:         Didier Trosset
//  Label:      Acqiris Confidential
//
//////////////////////////////////////////////////////////////////////////////////////////

#ifndef AQLIO_H
#define AQLIO_H

/*
 * This header file includes visa.h, and defines additionnal specific
 * functions or attributes.
 *
 */

#include "visa.h"


#if defined(__cplusplus) || defined(__cplusplus__)
   extern "C" {
#endif


ViStatus _VI_FUNC viOpenSimulatedRM(ViPSession vi);

#define VI_AQLIO_DMA0_SPACE (201)

#define VI_AQLIO_ADDR_DMA (201)


#if defined(__cplusplus) || defined(__cplusplus__)
   }
#endif


#endif // sentry

