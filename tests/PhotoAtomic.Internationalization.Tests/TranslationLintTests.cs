namespace PhotoAtomic.Tests;

/// <summary>
/// Every case here is a defect that actually reached a running game, or a
/// legitimate translation that an earlier version of the lint accused wrongly.
/// The second kind matters as much as the first: a lint nobody trusts is a
/// lint nobody reads.
/// </summary>
public class TranslationLintTests
{
    private static readonly string[] Sentences =
    [
        "Close the {0}",
        "It is a big and bulky {0}, but you could still carry it.",
        "Put the {0} into the {1}",
        "{0} uses the {1} with the {2}",
        "Something about it feels remarkably strong.",
    ];

    private static IReadOnlyList<LintFinding> Inspect(params TranslationRow[] rows) =>
        TranslationLint.Inspect(rows, Sentences, "en-US");

    [Fact]
    public void A_hole_that_never_arrives_and_one_that_should_not_be_there_are_both_caught()
    {
        var findings = Inspect(
            new TranslationRow("Put the {0} into the {1}", null, "it-IT", "Metti la {0} dentro", null),
            new TranslationRow("Something about it feels remarkably strong.", null, "it-IT", "Qualcosa in {0} sembra forte.", null));

        Assert.Contains(findings, finding => finding.Rule == TranslationLint.Rules.MissingHole);
        Assert.Contains(findings, finding => finding.Rule == TranslationLint.Rules.StrayHole);
        Assert.All(findings, finding => Assert.Equal(LintSeverity.Error, finding.Severity));
    }

    [Fact]
    public void Variants_without_a_plain_row_are_an_error_because_the_uncovered_case_falls_back()
    {
        var findings = Inspect(
            new TranslationRow("Close the {0}", "0:GENDER-female", "it-IT", "Chiudi la {0}", null),
            new TranslationRow("Close the {0}", "0:GENDER-male", "it-IT", "Chiudi il {0}", null));

        var missing = Assert.Single(findings, finding => finding.Rule == TranslationLint.Rules.NoFallbackRow);
        Assert.Equal(LintSeverity.Error, missing.Severity);

        // Add the plain row and the complaint goes away.
        Assert.DoesNotContain(
            Inspect(
                new TranslationRow("Close the {0}", "0:GENDER-female", "it-IT", "Chiudi la {0}", null),
                new TranslationRow("Close the {0}", "0:GENDER-male", "it-IT", "Chiudi il {0}", null),
                new TranslationRow("Close the {0}", null, "it-IT", "Chiudi {0}", null)),
            finding => finding.Rule == TranslationLint.Rules.NoFallbackRow);
    }

    [Fact]
    public void A_value_with_no_gender_is_caught_where_the_language_declines()
    {
        // The defect that made a whole room speak English: one name out of
        // forty forgot to say what gender it was.
        var findings = Inspect(
            new TranslationRow("Water", null, "it-IT", "acqua", "GENDER-female"),
            new TranslationRow("Stone press base", null, "it-IT", "base della pressa", null));

        var genderless = Assert.Single(findings, finding => finding.Rule == TranslationLint.Rules.GenderlessValue);
        Assert.Equal("Stone press base", genderless.Key);
    }

    [Fact]
    public void English_asks_nothing_about_gender_because_the_table_says_it_does_not_decline()
    {
        var findings = TranslationLint.Inspect(
            [
                new TranslationRow("Water", null, "en-GB", "water", null),
                new TranslationRow("Stone press base", null, "en-GB", "stone press base", null),
            ],
            Sentences,
            "en-US");

        Assert.DoesNotContain(findings, finding => finding.Rule == TranslationLint.Rules.GenderlessValue);
    }

    [Fact]
    public void The_real_agreement_defect_is_flagged_and_plain_elision_is_not()
    {
        // Flagged: the adjective and the pronoun agree for vowel-words and not
        // for the others — the model declined half the cases.
        var half = Inspect(
            new TranslationRow("It is a big and bulky {0}, but you could still carry it.", "0:GENDER-female", "it-IT", "È un grosso e ingombrante {0}, ma potresti portarlo.", null),
            new TranslationRow("It is a big and bulky {0}, but you could still carry it.", "0:GENDER-male", "it-IT", "È un grosso e ingombrante {0}, ma potresti portarlo.", null),
            new TranslationRow("It is a big and bulky {0}, but you could still carry it.", "0:GENDER-female,0:starts-with-vowel", "it-IT", "È una grossa e ingombrante {0}, ma potresti portarla.", null),
            new TranslationRow("It is a big and bulky {0}, but you could still carry it.", "0:GENDER-male,0:starts-with-vowel", "it-IT", "È un grosso e ingombrante {0}, ma potresti portarlo.", null),
            new TranslationRow("It is a big and bulky {0}, but you could still carry it.", null, "it-IT", "È un grosso e ingombrante {0}.", null));

        var flagged = Assert.Single(half, finding => finding.Rule == TranslationLint.Rules.InconsistentAgreement);
        Assert.Equal(LintSeverity.Warning, flagged.Severity);

        // Not flagged: only the article changes, and elision makes the two
        // genders collapse in front of a vowel. That is Italian, not a bug.
        var elision = Inspect(
            new TranslationRow("Close the {0}", "0:GENDER-female", "it-IT", "Chiudi la {0}", null),
            new TranslationRow("Close the {0}", "0:GENDER-male", "it-IT", "Chiudi il {0}", null),
            new TranslationRow("Close the {0}", "0:GENDER-female,0:starts-with-vowel", "it-IT", "Chiudi l'{0}", null),
            new TranslationRow("Close the {0}", "0:GENDER-male,0:starts-with-vowel", "it-IT", "Chiudi l'{0}", null),
            new TranslationRow("Close the {0}", null, "it-IT", "Chiudi {0}", null));

        Assert.DoesNotContain(elision, finding => finding.Rule == TranslationLint.Rules.InconsistentAgreement);
    }

    [Fact]
    public void A_subject_that_does_not_decline_the_verb_is_not_a_defect()
    {
        // Italian writes "usa la chiave" whoever is using it: two variants
        // differing only in the subject's gender are rightly identical.
        var findings = Inspect(
            new TranslationRow("{0} uses the {1} with the {2}", "0:GENDER-female,1:GENDER-female,2:GENDER-male", "it-IT", "{0} usa la {1} con il {2}", null),
            new TranslationRow("{0} uses the {1} with the {2}", "0:GENDER-male,1:GENDER-female,2:GENDER-male", "it-IT", "{0} usa la {1} con il {2}", null),
            new TranslationRow("{0} uses the {1} with the {2}", null, "it-IT", "{0} usa {1} con {2}", null));

        Assert.Empty(findings);
    }

    [Fact]
    public void The_missing_plain_row_is_the_least_committed_variant()
    {
        TranslationRow[] variants =
        [
            new("Close the {0}", "0:GENDER-female,0:starts-with-vowel", "it-IT", "Chiudi l'{0}", null),
            new("Close the {0}", "0:GENDER-male", "it-IT", "Chiudi il {0}", null),
        ];

        var completed = TranslationLint.WithFallback(variants);

        var plain = Assert.Single(completed, row => row.Context is null);
        Assert.Equal("Chiudi il {0}", plain.Template); // the one asking fewer criteria

        // Nothing to add when a plain row is already there.
        Assert.Equal(
            completed.Count,
            TranslationLint.WithFallback(completed).Count);
    }

    [Fact]
    public void Genders_that_merge_under_elision_are_not_a_contradiction_with_the_ones_that_do_not()
    {
        // French: "à l'" before a vowel whatever the gender, "à la" / "au"
        // before a consonant. Both rows are right, and an earlier version of
        // this rule called the pair a contradiction — half the complaints on
        // the real corpus were this, which is how a lint stops being read.
        var findings = Inspect(
            new TranslationRow("Put the {0} into the {1}", null, "fr-FR", "Mets {0} dans {1}", null),
            new TranslationRow("Put the {0} into the {1}", "1:GENDER-female", "fr-FR", "Mets le {0} dans la {1}", null),
            new TranslationRow("Put the {0} into the {1}", "1:GENDER-male", "fr-FR", "Mets le {0} dans le {1}", null),
            new TranslationRow("Put the {0} into the {1}", "1:GENDER-female,1:starts-with-vowel", "fr-FR", "Mets le {0} dans l'{1}", null),
            new TranslationRow("Put the {0} into the {1}", "1:GENDER-male,1:starts-with-vowel", "fr-FR", "Mets le {0} dans l'{1}", null));

        Assert.DoesNotContain(findings, finding => finding.Rule == TranslationLint.Rules.InconsistentAgreement);
    }

    [Fact]
    public void A_value_capitalized_in_one_language_and_not_in_another_is_a_common_noun_in_disguise()
    {
        // The real defect: the model called the bonfire a proper name in
        // Italian only, and "Falò" turned up in the middle of every sentence.
        var findings = Inspect(
            new TranslationRow("Bonfire", null, "it-IT", "falò", "GENDER-male,Capitalize"),
            new TranslationRow("Bonfire", null, "fr-FR", "feu de camp", "GENDER-male"),
            new TranslationRow("Bucket", null, "it-IT", "secchio", "GENDER-male"),
            new TranslationRow("Bucket", null, "fr-FR", "seau", "GENDER-male"),
            // A real proper name: capitalized by everyone, and nobody complains.
            new TranslationRow("The Pirate Galley", null, "it-IT", "La Galea Pirata", "GENDER-female,Capitalize"),
            new TranslationRow("The Pirate Galley", null, "fr-FR", "La Galère des Pirates", "GENDER-female,Capitalize"));

        var disputed = Assert.Single(findings, finding => finding.Rule == TranslationLint.Rules.DisputedCapitalization);
        Assert.Equal("Bonfire", disputed.Key);
        Assert.Equal("it-IT", disputed.Language);
        Assert.Equal(LintSeverity.Warning, disputed.Severity);
    }

    [Fact]
    public void A_language_that_capitalizes_every_noun_is_not_accused_of_it()
    {
        // German writes "Eimer" and "Lagerfeuer" with capitals because German
        // does that, not because a model got confused. The corpus says so.
        var findings = Inspect(
            new TranslationRow("Bonfire", null, "de-DE", "Lagerfeuer", "GENDER-male,Capitalize"),
            new TranslationRow("Bonfire", null, "it-IT", "falò", "GENDER-male"),
            new TranslationRow("Bucket", null, "de-DE", "Eimer", "GENDER-male,Capitalize"),
            new TranslationRow("Bucket", null, "it-IT", "secchio", "GENDER-male"),
            new TranslationRow("Water", null, "de-DE", "Wasser", "GENDER-male,Capitalize"),
            new TranslationRow("Water", null, "it-IT", "acqua", "GENDER-female"));

        Assert.DoesNotContain(findings, finding => finding.Rule == TranslationLint.Rules.DisputedCapitalization);
    }

    [Fact]
    public void A_dead_sentence_is_reported_once_and_not_picked_apart()
    {
        // Its rows may well be missing a fallback and anything else: nobody
        // reads them, and listing their defects buries the ones that matter.
        var findings = Inspect(
            new TranslationRow("It sloshes: this is a liquid.", "0:GENDER-female", "it-IT", "Sciaborda.", null),
            new TranslationRow("It sloshes: this is a liquid.", "0:GENDER-male", "it-IT", "Sciaborda.", null));

        Assert.All(findings, finding => Assert.Equal(TranslationLint.Rules.OrphanRow, finding.Rule));
    }

    [Fact]
    public void A_sentence_no_code_asks_for_is_reported_as_dead_weight()
    {
        var findings = Inspect(
            new TranslationRow("It sloshes: this is a liquid.", null, "it-IT", "Sciaborda: è un liquido.", null));

        var orphan = Assert.Single(findings, finding => finding.Rule == TranslationLint.Rules.OrphanRow);
        Assert.Equal(LintSeverity.Warning, orphan.Severity);
    }

    [Fact]
    public void A_word_the_sentence_itself_names_is_not_mistaken_for_a_leftover_example()
    {
        // "steam rises" is allowed to mention steam; "nel falò" is not, when
        // the English key says nothing about a bonfire.
        var findings = Inspect(
            new TranslationRow("Steam", null, "it-IT", "vapore", "GENDER-male"),
            new TranslationRow("Bonfire", null, "it-IT", "falò", "GENDER-male"),
            new TranslationRow("Put the {0} into the {1}", null, "it-IT", "Infila la {0} nel falò", null));

        var leftover = Assert.Single(findings, finding => finding.Rule == TranslationLint.Rules.ExampleLeftIn);
        Assert.Contains("falò", leftover.Message);
    }
}
