using System.Collections.Generic;

namespace OpalLocation.Models
{
    public struct TripInfo
    {
        public ulong tripID { get; set; }
        public int direction { get; set; }

        public TripInfo(ulong tripID, string direction, List<string> directionList, Dictionary<string, int> usedDirections)
        {
            this.tripID = tripID;
            if (usedDirections.ContainsKey(direction))
                this.direction = usedDirections[direction];
            else
            {
                directionList.Add(direction);
                var index = directionList.Count - 1;
                usedDirections[direction] = index;
                this.direction = index;
            }
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
        public string occupancy { get; set; }

    }

    public class TripDataSet
    {
        public Dictionary<string, TripInfo[]> trips;
        public Dictionary<ulong, uint[]> tripStops;
        public Dictionary<uint, Coordinate> stops;
        public List<string> directionNames;

        public TripDataSet((Dictionary<string, TripInfo[]>, List<string>) tripData, Dictionary<ulong, uint[]> tripStops, Dictionary<uint, Coordinate> stops)
        {
            this.trips = tripData.Item1;
            this.directionNames = tripData.Item2;
            this.tripStops = tripStops;
            this.stops = stops;
        }
    }
}
