/*
* Copyright (c) Krikzz and Contributors.
* See LICENSE file in the project root for full license information.
*/

#ifndef __MAIN_H
#define __MAIN_H

#ifdef __cplusplus
extern "C" {
#endif

#include "ed64.h"
#include "ff.h"
#include "screen.h"
#include "sddisk.h"


void perform_simulated_rom_boot(u8 cic);
u8 file_menu_display();
void usb_terminal_display();
void usb_command_display_load_rom();
u8 file_display_read();
u8 file_display_write();

#ifdef __cplusplus
}
#endif

#endif /* MAIN_H */
