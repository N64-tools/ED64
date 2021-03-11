/*
* Copyright (c) Krikzz and Contributors.
* See LICENSE file in the project root for full license information.
*/

#include "main.h"

u8 file_display_read() {

  u8 *path = "/ED64/OS64.v64";
  struct controller_data cd;
  u8 buff[256];
  FIL f;
  UINT br;
  u8 resp;

  screen_clear();
  screen_repaint();

  resp = f_open(&f, path, FA_READ);
  if (resp != FR_OK)
    return resp;

  resp = f_read(&f, buff, sizeof(buff), &br);
  if (resp != FR_OK) {
  //  return resp; //we should continue so that the file is closed!
      screen_print("Error reading file.");
  }

  resp = f_close(&f);
  if (resp != FR_OK)
    return resp;

  screen_clear();
  screen_print("Data read from: ");
  screen_append_string(path);
  screen_print("Press B to exit");

  screen_print("");
  for (int i = 0; i < sizeof(buff); i++) {
    if (i % 16 == 0)
      screen_print("");
    screen_append_hex8(buff[i]);
  }

  screen_repaint();
  for (;;) { /* forever */
    screen_perform_vsync();
    controller_scan();
    cd = get_keys_down();

    if (cd.c[0].B) {
      break;
    }
  }

  return 0;
}

u8 file_display_write() {

  u8 *path = "test.txt";
  u8 *msg = "This is test message";
  struct controller_data cd;
  FIL f;
  UINT bw;
  u8 resp;
  u32 str_len;

  screen_clear();
  screen_repaint();

  for (str_len = 0; msg[str_len] != 0; str_len++)
    ;

  resp = f_open(&f, path, FA_WRITE | FA_CREATE_ALWAYS);
  if (resp != FR_OK)
    return resp;

  resp = f_write(&f, msg, str_len, &bw);
  if (resp != FR_OK)
    return resp;

  resp = f_close(&f);
  if (resp != FR_OK)
    return resp;

  screen_clear();
  screen_print(msg);
  screen_print("");
  screen_print("String above was written to: ");
  screen_print(path);
  screen_print("Press B to exit");

  screen_repaint();
  for (;;) { /* forever */
    screen_perform_vsync();
    controller_scan();
    cd = get_keys_down();

    if (cd.c[0].B) {
      break;
    }
  }

  return 0;
}