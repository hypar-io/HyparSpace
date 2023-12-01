using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Elements.Geometry;

namespace TravelDistanceAnalyzer
{
    internal static class ColorFactory
    {
        public static Color FromGuid(string guid)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(guid);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                byte r = hashBytes[0];
                byte g = hashBytes[1];
                byte b = hashBytes[2];

                return new Color(r / 255.0, g / 255.0, b / 255.0, 1d);
            }
        }
    }
}
