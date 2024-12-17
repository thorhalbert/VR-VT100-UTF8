This originated at least 15 year ago from a project at code.google.com (which is gone)
It was forked by this project: https://github.com/taterbase/libvt100
And that was forked by Thor Halbert (Coolearth Employee): https://github.com/thorhalbert/VR-VT100-UTF8
as a VR project to virtualize terminals in VR.  Since it basically was an engine to virtualize terminals
it seemed like a good start.

I got mostly finished with vt100 emulation and some ansi and xterm emulation, but these later, along
with dynamic sizing might need some work.   But this should work for Whistle, which is very simple vt100.
I will push any bugfixes back into VR-VT100-UTF8.

This will also likely be extended for events for changes and movement of the cursor and modernized
for latest .net core.

I was thinking this should be async, but it's not much of an performance advantage in a compute
bound operation (filing the frame buffer).

---Thor Halbert (8/2024)
