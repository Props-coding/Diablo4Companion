using D4Companion.Crafting;

namespace D4Companion.Crafting.Tests
{
    public class GearStoreTests
    {
        private string _directory = string.Empty;
        private string _file = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "d4c-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _file = Path.Combine(_directory, "gear.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }

        private static GearSnapshot Ring(double life = 900) => new()
        {
            ItemType = "ring",
            ItemPower = 800,
            Rarity = "Legendary",
            IsConfirmed = true,
            Affixes = new[]
            {
                new ScannedAffix { AffixId = "MaxLife", DisplayName = "Maximum Life", Kind = AffixKind.Normal, Value = life, TuningPrisms = new[] { "Defensive" } },
                new ScannedAffix { AffixId = "CritChance", DisplayName = "Critical Strike Chance", Kind = AffixKind.Greater, Value = 5 }
            }
        };

        [Test]
        public void SaveAndReload_KeepsHistory()
        {
            var store = new GearStore(_file);
            var saved = store.AddItem(Ring());
            Assert.That(saved.Success, Is.True);
            store.AddRevision(saved.Record!.Id, Ring(1200));

            var reloaded = new GearStore(_file);
            reloaded.Load();

            var record = reloaded.Records.Single();
            Assert.That(record.Revisions, Has.Count.EqualTo(2));
            Assert.That(record.Latest!.Snapshot.Affixes[0].Value, Is.EqualTo(1200));
            Assert.That(record.Revisions[0].Snapshot.Affixes[0].Value, Is.EqualTo(900));
            Assert.That(record.Latest.Snapshot.Affixes[1].Kind, Is.EqualTo(AffixKind.Greater));
            Assert.That(reloaded.LoadIssues, Is.Empty);
        }

        [Test]
        public void CorruptedFile_IsSetAsideAndStoreStartsEmpty()
        {
            File.WriteAllText(_file, "{ this is not json ");

            var store = new GearStore(_file);
            Assert.DoesNotThrow(() => store.Load());

            Assert.That(store.Records, Is.Empty);
            Assert.That(store.LoadIssues, Has.Count.EqualTo(1));
            Assert.That(Directory.GetFiles(_directory, "gear.corrupt-*.json"), Has.Length.EqualTo(1));

            // The store keeps working after recovery.
            Assert.That(store.AddItem(Ring()).Success, Is.True);
        }

        [Test]
        public void WrongShape_IsTreatedAsCorrupted()
        {
            File.WriteAllText(_file, "[1, 2, 3]");

            var store = new GearStore(_file);
            store.Load();

            Assert.That(store.Records, Is.Empty);
            Assert.That(store.LoadIssues, Is.Not.Empty);
        }

        [Test]
        public void InvalidEntries_AreSkippedButValidOnesKept()
        {
            File.WriteAllText(_file, """
            {
              "Version": 1,
              "Items": [
                { "Id": "11111111-1111-1111-1111-111111111111", "Revisions": [ { "Label": "x", "Snapshot": { "ItemType": "", "ItemPower": 800 } } ] },
                { "Id": "22222222-2222-2222-2222-222222222222", "Revisions": [ { "Label": "ok", "Snapshot": { "ItemType": "ring", "ItemPower": 800, "Affixes": [ { "AffixId": "MaxLife", "Kind": "Normal", "Value": 10 } ] } } ] },
                { "Id": "33333333-3333-3333-3333-333333333333", "Revisions": [ { "Label": "bad power", "Snapshot": { "ItemType": "ring", "ItemPower": -5 } } ] },
                null
              ]
            }
            """);

            var store = new GearStore(_file);
            store.Load();

            Assert.That(store.Records, Has.Count.EqualTo(1));
            Assert.That(store.Records[0].ItemType, Is.EqualTo("ring"));
            Assert.That(store.LoadIssues, Has.Count.EqualTo(2));
        }

        [Test]
        public void InvalidSnapshot_IsNotSaved()
        {
            var store = new GearStore(_file);

            var result = store.AddItem(new GearSnapshot { ItemType = "", ItemPower = 800 });

            Assert.That(result.Success, Is.False);
            Assert.That(store.Records, Is.Empty);
            Assert.That(File.Exists(_file), Is.False);
        }

        [Test]
        public void Revision_MustBeSameSlot()
        {
            var store = new GearStore(_file);
            var saved = store.AddItem(Ring());

            var result = store.AddRevision(saved.Record!.Id, Ring() with { ItemType = "helm" });

            Assert.That(result.Success, Is.False);
            Assert.That(store.Records.Single().Revisions, Has.Count.EqualTo(1));
        }

        [Test]
        public void SavedSnapshot_DoesNotChangeWhenCallerChangesItsList()
        {
            var store = new GearStore(_file);
            var affixes = new List<ScannedAffix> { new() { AffixId = "MaxLife", Kind = AffixKind.Normal, Value = 1 } };
            store.AddItem(new GearSnapshot { ItemType = "ring", ItemPower = 800, Affixes = affixes });

            affixes.Add(new ScannedAffix { AffixId = "Thorns", Kind = AffixKind.Normal });

            Assert.That(store.Records.Single().Latest!.Snapshot.Affixes, Has.Count.EqualTo(1));
        }

        [Test]
        public void Compare_ShowsBeforeAndAfter()
        {
            var before = Ring(900);
            var after = Ring(1200) with
            {
                Affixes = new[]
                {
                    new ScannedAffix { AffixId = "MaxLife", Kind = AffixKind.Normal, Value = 1200 },
                    new ScannedAffix { AffixId = "AttackSpeed", Kind = AffixKind.Normal, Value = 8 }
                }
            };

            var diff = SnapshotComparer.Compare(before, after);

            Assert.That(diff.Single(d => d.Before?.AffixId == "MaxLife").Change, Is.EqualTo(AffixChange.ValueChanged));
            Assert.That(diff.Single(d => d.Before?.AffixId == "CritChance").Change, Is.EqualTo(AffixChange.Removed));
            Assert.That(diff.Single(d => d.After?.AffixId == "AttackSpeed").Change, Is.EqualTo(AffixChange.Added));
        }
    }
}
