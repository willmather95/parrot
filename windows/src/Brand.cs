using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Parrot.Windows
{
    // Shared with the native Mac candidate. Keep color choices in one place.
    internal static class Brand
    {
        internal static readonly Color Background = SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(24, 23, 20);
        internal static readonly Color Panel = SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(32, 31, 27);
        internal static readonly Color Raised = SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(43, 41, 35);
        internal static readonly Color Cream = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(238, 232, 213);
        internal static readonly Color Muted = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(183, 175, 154);
        internal static readonly Color Amber = SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(240, 174, 82);
        internal static readonly Color Red = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(226, 117, 100);
        internal static readonly Color Line = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(82, 76, 64);

        internal static Icon LoadIcon()
        {
            using (Stream stream = Resource("Parrot.Windows.BrandIcon"))
            using (Icon source = new Icon(stream))
            {
                return (Icon)source.Clone();
            }
        }

        internal static Image LoadArtwork()
        {
            using (Stream stream = Resource("Parrot.Windows.BrandArtwork"))
            using (Image source = Image.FromStream(stream))
            {
                return new Bitmap(source);
            }
        }

        private static Stream Resource(string name)
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream == null) throw new InvalidOperationException("Required Parrot branding resource is missing: " + name);
            return stream;
        }
    }
}
