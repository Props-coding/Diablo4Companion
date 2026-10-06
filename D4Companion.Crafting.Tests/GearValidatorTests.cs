using D4Companion.Crafting;

namespace D4Companion.Crafting.Tests
{
    public class GearValidatorTests
    {
        [Test]
        public void Sanitize_RemovesInvalidNumbersAndTrimsText()
        {
            var snapshot = new GearSnapshot
            {
                ItemType = "  Ring \n",
                ItemPower = 800,
                Affixes = new[]
                {
                    new ScannedAffix { AffixId = " MaxLife ", Kind = AffixKind.Normal, Value = double.NaN },
                    new ScannedAffix { AffixId = "Thorns", Kind = (AffixKind)42, Value = double.PositiveInfinity, OcrText = new string('x', 1000) }
                }
            };

            var clean = GearValidator.Sanitize(snapshot);

            Assert.That(clean.ItemType, Is.EqualTo("ring"));
            Assert.That(clean.Affixes[0].AffixId, Is.EqualTo("MaxLife"));
            Assert.That(clean.Affixes[0].Value, Is.Null);
            Assert.That(clean.Affixes[1].Value, Is.Null);
            Assert.That(clean.Affixes[1].Kind, Is.EqualTo(AffixKind.Unknown));
            Assert.That(clean.Affixes[1].OcrText, Has.Length.EqualTo(GearValidator.MaxTextLength));
        }

        [Test]
        public void Sanitize_KeepsLongGameDataAffixIdsWhole()
        {
            // Game data ids join every internal id of an affix with ';' and can run to thousands of characters.
            string longId = string.Join(";", Enumerable.Range(0, 400).Select(i => $"CoreStat_Willpower_{i}"));
            var snapshot = new GearSnapshot
            {
                ItemType = "ring",
                ItemPower = 800,
                Affixes = new[] { new ScannedAffix { AffixId = longId, Kind = AffixKind.Greater } }
            };

            var clean = GearValidator.Sanitize(snapshot);

            Assert.That(clean.Affixes[0].AffixId, Is.EqualTo(longId));
        }

        [Test]
        public void UnknownAffix_CanBeSavedButNotConfirmed()
        {
            var snapshot = new GearSnapshot
            {
                ItemType = "ring",
                ItemPower = 800,
                Affixes = new[] { new ScannedAffix { OcrText = "garbled", Kind = AffixKind.Normal } }
            };

            var result = GearValidator.Validate(snapshot);

            Assert.That(result.CanSave, Is.True);
            Assert.That(result.CanConfirm, Is.False);
        }

        [Test]
        public void DuplicateAffix_BlocksConfirm()
        {
            var snapshot = new GearSnapshot
            {
                ItemType = "ring",
                ItemPower = 800,
                Affixes = new[]
                {
                    new ScannedAffix { AffixId = "MaxLife", Kind = AffixKind.Normal },
                    new ScannedAffix { AffixId = "maxlife", Kind = AffixKind.Greater }
                }
            };

            var result = GearValidator.Validate(snapshot);

            Assert.That(result.CanConfirm, Is.False);
            Assert.That(result.Issues.Single().AffixIndex, Is.EqualTo(1));
        }

        [Test]
        public void SameAffixAsImplicitAndNormal_IsNotDuplicate()
        {
            var snapshot = new GearSnapshot
            {
                ItemType = "ring",
                ItemPower = 800,
                Affixes = new[]
                {
                    new ScannedAffix { AffixId = "Resistance", Kind = AffixKind.Implicit },
                    new ScannedAffix { AffixId = "Resistance", Kind = AffixKind.Normal }
                }
            };

            Assert.That(GearValidator.Validate(snapshot).CanConfirm, Is.True);
        }

        [TestCase(-1)]
        [TestCase(GearValidator.MaxItemPower + 1)]
        public void InvalidItemPower_CannotBeSaved(int power)
        {
            var result = GearValidator.Validate(new GearSnapshot { ItemType = "ring", ItemPower = power });

            Assert.That(result.CanSave, Is.False);
        }

        [Test]
        public void TooManyAffixes_CannotBeSaved()
        {
            var affixes = Enumerable.Range(0, GearValidator.MaxAffixes + 1)
                .Select(i => new ScannedAffix { AffixId = "A" + i, Kind = AffixKind.Normal })
                .ToArray();

            var result = GearValidator.Validate(new GearSnapshot { ItemType = "ring", ItemPower = 800, Affixes = affixes });

            Assert.That(result.CanSave, Is.False);
        }
    }
}
