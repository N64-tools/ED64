# FatFS lib by http://www.elm-chan.org/fsw/ff/00index_e.html

Certain files in this folder should be updated periodically to the latest release.

The current version supplied in this folder is R0.14a_p2

diskio.h and diskio.c are heavily customised from the original templates for specific support.
ffconf.h has also been customized as required.

It is possible that fatFS causes file alignment issues as it is not cache-aware. A good discussion on it (and potential patch) can be found here:
http://forum.chibios.org/viewtopic.php?f=35&t=5760
