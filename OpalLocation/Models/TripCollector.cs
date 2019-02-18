using System.Collections.Generic;
using TransitRealtime;

namespace OpalLocation.Models
{
    public struct TripInfo
    {
        public ulong tripID { get; set; }
        public string direction { get; set; }

        public TripInfo(ulong tripID, string direction)
        {
            this.tripID = tripID;
            this.direction = direction;
        }
    }

    public struct Coordinate
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
            if (ReferenceEquals(this, obj))
                return true;
            if (!(obj is Coordinate cor))
                return false;
            return (this.latitude == cor.latitude && this.longitude == cor.longitude);
        }
    }

    public struct TripLoc
    {
        public Coordinate coordinate { get; set; }
        public VehiclePosition.Types.OccupancyStatus occupancy { get; set; }
    }

    public class TripDataSet
    {
        public Dictionary<string, TripInfo[]> trips;
        public Dictionary<ulong, uint[]> tripStops;
        public Dictionary<uint, Coordinate> stops;

        public TripDataSet()
        {
            trips = new Dictionary<string, TripInfo[]>();
            tripStops = new Dictionary<ulong, uint[]>();
            stops = new Dictionary<uint, Coordinate>();
        }

        public TripDataSet(Dictionary<string, TripInfo[]> tripData, Dictionary<ulong, uint[]> tripStops, Dictionary<uint, Coordinate> stops)
        {
            this.trips = tripData;
            this.tripStops = tripStops;
            this.stops = stops;
        }
    }
}
