//
// Copyright (c) Krikzz and Contributors.
// See LICENSE file in the project root for full license information.
//

#ifndef __ED64_EVERDRIVE_H
#define	__ED64_EVERDRIVE_H

#ifdef __cplusplus
extern "C" {
#endif

#include "sysregion.h"
#include "bios.h"
#include "disk.h"
#include "ff.h"
#include "graphics.h"
#include "libdragon.h"
#include "types.h"

void boot_simulator(u8 cic);
u8 fmanager();
void usbTerminal();
void usbLoadGame();
u8 fileRead();
u8 fileWrite();

#ifdef __cplusplus
}
#endif

#endif	/* EVERDRIVE_H */
