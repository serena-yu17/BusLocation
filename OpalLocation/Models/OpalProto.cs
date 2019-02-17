using ProtoBuf;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace OpalLocation.Models
{
    [ProtoContract]
    public enum Incrementality
    {
        [ProtoEnum]
        FULL_DATASET = 0,
        [ProtoEnum]
        DIFFERENTIAL = 1
    }

    [ProtoContract]
    public enum ScheduleRelationship
    {
        [ProtoEnum]
        SCHEDULED = 0,
        [ProtoEnum]
        SKIPPED = 1,
        [ProtoEnum]
        NO_DATA = 2
    }

    [ProtoContract]
    public enum VehicleStopStatus
    {
        [ProtoEnum]
        INCOMING_AT = 0,
        [ProtoEnum]
        STOPPED_AT = 1,
        [ProtoEnum]
        IN_TRANSIT_TO = 2
    }

    [ProtoContract]
    public enum CongestionLevel
    {
        [ProtoEnum]
        UNKNOWN_CONGESTION_LEVEL = 0,
        [ProtoEnum]
        RUNNING_SMOOTHLY = 1,
        [ProtoEnum]
        STOP_AND_GO = 2,
        [ProtoEnum]
        CONGESTION = 3,
        [ProtoEnum]
        SEVERE_CONGESTION = 4
    }

    [ProtoContract]
    public enum OccupancyStatus
    {
        [ProtoEnum]
        EMPTY = 0,
        [ProtoEnum]
        MANY_SEATS_AVAILABLE = 1,
        [ProtoEnum]
        FEW_SEATS_AVAILABLE = 2,
        [ProtoEnum]
        STANDING_ROOM_ONLY = 3,
        [ProtoEnum]
        CRUSHED_STANDING_ROOM_ONLY = 4,
        [ProtoEnum]
        FULL = 5,
        [ProtoEnum]
        NOT_ACCEPTING_PASSENGERS = 6
    }

    [ProtoContract]
    public enum Cause
    {
        [ProtoEnum]
        UNKNOWN_CAUSE = 1,
        [ProtoEnum]
        OTHER_CAUSE = 2,        // Not machine-representable.
        [ProtoEnum]
        TECHNICAL_PROBLEM = 3,
        [ProtoEnum]
        STRIKE = 4,             // Public transit agency employees stopped working.
        [ProtoEnum]
        DEMONSTRATION = 5,      // People are blocking the streets.
        [ProtoEnum]
        ACCIDENT = 6,
        [ProtoEnum]
        HOLIDAY = 7,
        [ProtoEnum]
        WEATHER = 8,
        [ProtoEnum]
        MAINTENANCE = 9,
        [ProtoEnum]
        CONSTRUCTION = 10,
        [ProtoEnum]
        POLICE_ACTIVITY = 11,
        [ProtoEnum]
        MEDICAL_EMERGENCY = 12,
    }

    [ProtoContract]
    public enum Effect
    {
        [ProtoEnum]
        NO_SERVICE = 1,
        [ProtoEnum]
        REDUCED_SERVICE = 2,
        [ProtoEnum]
        SIGNIFICANT_DELAYS = 3,
        [ProtoEnum]
        DETOUR = 4,
        [ProtoEnum]
        ADDITIONAL_SERVICE = 5,
        [ProtoEnum]
        MODIFIED_SERVICE = 6,
        [ProtoEnum]
        OTHER_EFFECT = 7,
        [ProtoEnum]
        UNKNOWN_EFFECT = 8,
        [ProtoEnum]
        STOP_MOVED = 9,
    }

    [ProtoContract]
    public enum ScheduleTripRelationship
    {
        [ProtoEnum]
        SCHEDULED = 0,
        [ProtoEnum]
        ADDED = 1,
        [ProtoEnum]
        UNSCHEDULED = 2,
        [ProtoEnum]
        CANCELED = 3,
        [ProtoEnum]
        REPLACEMENT = 5
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    [ProtoInclude(100, typeof(FeedHeader))]
    [ProtoInclude(100, typeof(FeedEntity))]
    public class FeedMessage
    {
        public FeedHeader header;
        public FeedEntity[] entity;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    [ProtoInclude(100, typeof(Incrementality))]
    public class FeedHeader
    {
        public string gtfs_realtime_version;
        public Incrementality incrementality = Incrementality.DIFFERENTIAL;
        public ulong timestamp;
        public byte[] extension = null;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class FeedEntity
    {
        public string id;
        public bool is_deleted = false;
        public TripUpdate trip_update;
        public VehiclePosition vehicle;
        public Alert alert;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class TripUpdate
    {
        public TripDescriptor trip;
        public VehicleDescriptor vehicle;
        public StopTimeUpdate[] stop_time_update;
        public ulong timestamp = 0;
        public byte[] extensions;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class StopTimeEvent
    {
        public int delay;
        public long time;
        public int uncertainty;
        public byte[] extension;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class StopTimeUpdate
    {
        public uint stop_sequence = 0;
        public string stop_id;
        public StopTimeEvent arrival;
        public StopTimeEvent departure;
        public ScheduleRelationship schedule_relationship = ScheduleRelationship.SCHEDULED;
        public byte[] extension;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class VehiclePosition
    {
        public TripDescriptor trip;
        public VehicleDescriptor vehicle;
        public Position position;
        public uint current_stop_sequence = 0;
        public string stop_id;
        public VehicleStopStatus current_status = VehicleStopStatus.IN_TRANSIT_TO;
        public ulong timestamp;
        public CongestionLevel congestion_level;
        public OccupancyStatus occupancy_status;
        public byte[] extension;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class Alert
    {
        public TimeRange[] active_period;
        public EntitySelector[] informed_entity;
        public Cause cause = Cause.UNKNOWN_CAUSE;
        public Effect effect = Effect.UNKNOWN_EFFECT;
        public TranslatedString url;
        public TranslatedString header_text;
        public TranslatedString description_text;
        public byte[] extension;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class TimeRange
    {
        public ulong start;
        public ulong end;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class Position
    {
        public float latitude;
        public float longitude;
        public float bearing;
        public double odometer;
        public float speed;
        public byte[] extension;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class TripDescriptor
    {
        public string trip_id;
        public string route_id;
        public string start_time;
        public string start_date;
        public ScheduleTripRelationship schedule_relationship;
        public byte[] extension;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class VehicleDescriptor
    {
        public string id;
        public string label;
        public string license_plate;
        public byte[] extension;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class EntitySelector
    {
        public string agency_id;
        public string route_id;
        public int route_type;
        public TripDescriptor trip;
        public string stop_id;
        public byte[] extension;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class TranslatedString
    {
        Translation[] translation;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public class Translation
    {
        public string text;
        public string language;
    }
}
