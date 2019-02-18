using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OpalLocation.Models
{
    public class TripInfo
    {
        public ulong tripID { get; set; }
        public string direction { get; set; }

        public TripInfo(ulong tripID, string direction)
        {
            this.tripID = tripID;
            this.direction = direction;
        }
    }

    public class Coordinate
    {
        public float latitude { get; set; }
        public float longitude { get; set; }
        public Coordinate(float lat, float lon)
        {
            latitude = lat;
            longitude = lon;
        }

        public override int GetHashCode()
        {
            return (latitude.GetHashCode() << 16) | longitude.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (this == null && obj == null)
                return true;
            if (this == null && obj != null)
                return false;
            if (this != null && obj == null)
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (!(obj is Coordinate))
                return false;
            var cor = obj as Coordinate;
            return (this.latitude == cor.latitude && this.longitude == cor.longitude);
        }
    }

    public class TripLoc
    {
        public Coordinate coordinate { get; set; }
        public string occupancy { get; set; }
    }

    public struct TripDataSet
    {

    }
}
