﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AudioAlign.FFmpeg {
    internal class Interop64Wrapper :IInteropWrapper {
        public IntPtr stream_open(string filename) {
            return Interop64.stream_open(filename);
        }

        public IntPtr stream_get_output_config(IntPtr instance) {
            return Interop64.stream_get_output_config(instance);
        }

        public int stream_read_frame(IntPtr instance) {
            return Interop64.stream_read_frame(instance);
        }

        public void stream_close(IntPtr instance) {
            Interop64.stream_close(instance);
        }
    }
}
