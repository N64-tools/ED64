/*
* Copyright (c) Krikzz and Contributors.
* See LICENSE file in the project root for full license information.
*/

#include "main.h"

u8 file_menu_load_directory(u8 *path, FILINFO *inf, u32 max_items);
u8 file_menu_load_rom(u8 *path);

#define MAXIMUM_DIRECTORY_COUNT 20
#define MAXIMUM_STRING_LENGTH 36

u8 file_menu_display() {

  FILINFO inf[MAXIMUM_DIRECTORY_COUNT];
  u32 selector = 0;
  struct controller_data cd;
  u8 resp;

  /* open root dir */
  resp = file_menu_load_directory("", inf, MAXIMUM_DIRECTORY_COUNT);
  if (resp)
    return resp;

  for (;;) { /* forever */

    /* print items */
    screen_clear();
    for (int i = 0; i < MAXIMUM_DIRECTORY_COUNT && inf[i].fname[0]; i++) {
      screen_print(selector == i ? ">" : " ");
      u8 tmp = inf[i].fname[MAXIMUM_STRING_LENGTH];
      inf[i].fname[MAXIMUM_STRING_LENGTH] =
          0; /* make sure that the printed string doesn't exceed max len */
      screen_append_string(inf[i].fname);
      inf[i].fname[MAXIMUM_STRING_LENGTH] = tmp;
    }

    screen_repaint();

    /* controls */
    for (;;) { /* forever */

      screen_perform_vsync();
      controller_scan();
      cd = get_keys_down();

      if (cd.c[0].B)
        return 0;

      if (cd.c[0].up) {
        if (selector != 0)
          selector--;
        break;
      }

      if (cd.c[0].down) {
        if ((selector + 1) < MAXIMUM_DIRECTORY_COUNT &&
            inf[selector + 1].fname[0])
          selector++;
        break;
      }

      if (cd.c[0].A && !(inf[selector].fattrib & AM_DIR)) {

        screen_clear();
        screen_print("loading...");
        screen_repaint();

        resp = file_menu_load_rom(inf[selector].fname);
        if (resp)
          return resp;

        ed64_set_rom_save_type(ED64_SAVE_TYPE_EEP16K); /* set save type */
        perform_simulated_rom_boot(CIC_6102);          /* run the rom */
      }
    }
  }

  return 0;
}

u8 file_menu_load_directory(u8 *path, FILINFO *inf, u32 max_items) {

  u8 resp;
  DIR dir;

  resp = f_opendir(&dir, path);
  if (resp != FR_OK)
    return resp;

  /* load directory items */
  for (int i = 0; i < max_items; i++) {

    resp = f_readdir(&dir, &inf[i]);
    if (resp)
      return resp;
    if (inf[i].fname[0] == 0)
      break; /* no directory items anymore */
  }

  resp = f_closedir(&dir);
  // if (resp)return resp;

  return resp;
}

u8 file_menu_load_rom(u8 *path) {

  FIL f;
  u8 resp;
  u8 header[8];
  UINT br;
  u32 fsize;

  resp = f_open(&f, path, FA_READ);
  if (resp != FR_OK)
    return resp;

  fsize = f.obj.objsize - f.fptr;

  /* read rom header */
  resp = f_read(&f, header, sizeof(header), &br);
  if (resp == FR_OK) {
    // return resp;

    /* set read position */
    resp = f_lseek(&f, 0);
    if (resp != FR_OK)
      return resp;

    if (header[1] == 0x80) {
      /* enable byte swapping for disk operations if rom image has swapped byte
      * order
      * affects only reading to ROM address space */
      ed64_rom_write_bytes_swapped(1);
    }

    /* WARNING! file can be read directly to rom but not to bram */
    resp = f_read(&f, (void *)ED64_ADDR_ROM, fsize, &br);
    if (resp != FR_OK)
      return resp;

    ed64_rom_write_bytes_swapped(0);
    if (resp != FR_OK)
      return resp;
  }
  resp = f_close(&f);
  // if (resp != FR_OK)return resp;

  return resp;
}
