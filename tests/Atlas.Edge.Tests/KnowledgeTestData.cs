using System.Collections.Immutable;
using Atlas.Edge.Knowledge;

namespace Atlas.Edge.Tests;

internal static class KnowledgeTestData
{
    public static KnowledgeRecord CreateRecord(Guid? recordId = null) =>
        new(
            recordId ?? Guid.NewGuid(),
            new Issue("Paper jam", "Repeated paper jam at the transport entrance.", "JAM-104"),
            [
                new Observation(
                    "Jam occurs after approximately 20 pages.",
                    new Timestamp(new DateTimeOffset(2026, 7, 31, 9, 30, 0, TimeSpan.FromHours(-7))))
            ],
            [
                new Evidence("event-log", "Transport sensor reported a blocked path.", "local-event-42")
            ],
            new Resolution("Replaced the feed roller and cleaned the transport path."),
            new Outcome(OutcomeStatus.Successful, "Completed a 500-page verification batch."),
            new Confidence(96m, "Recorded by the servicing engineer."),
            new RepairTime(TimeSpan.FromMinutes(11)),
            [new PartUsed("PA03576-K010", "Feed roller", 1)],
            new Firmware("2.4.1"),
            new Driver("fi Series Driver", "3.2.0"),
            new Scanner(
                new Manufacturer("Fujitsu"),
                new Model("fi-8170"),
                new Serial("SERIAL-8170-A")),
            new Customer("customer-a", "Example Customer"),
            new Site("site-seattle", "Seattle"),
            new Timestamp(new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.FromHours(-7))));
}
