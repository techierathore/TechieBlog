using Bunit;
using BlogModels.Models;
using BlogUI.Components.Resume;

namespace TechieBlog.Tests.Components.BlogUi;

/// <summary>
/// bUnit component tests for the public contact block shared by <c>/</c> and <c>/resume</c>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The site owner asked (2026-08-10) for the email address and the phone
/// number to stop being published, leaving LinkedIn as the only public contact route. Both
/// surfaces render this one component, so the rule is enforceable in exactly one place — and it
/// is the kind of rule that a later "helpful" edit reinstates without anyone noticing, because
/// re-adding a mailto: link looks like an improvement. These tests make that a failing build.</para>
///
/// <para><b>What is pinned:</b> that a user carrying an email address and a phone number produces
/// no <c>mailto:</c>, no <c>tel:</c> and neither value anywhere in the rendered markup; that the
/// LinkedIn call to action is rendered and opens safely in a new tab; and that the section's
/// render guard keys off what is actually DISPLAYED, so an owner with only an email address gets
/// no empty Contact card and an owner with only a LinkedIn URL still gets one.</para>
///
/// <para><b>Dependencies:</b> BlogUI and therefore TrBlazeUI; this suite compiles only under
/// <c>-p:IncludeBlogUiTests=true</c>, which is the default.</para>
/// </remarks>
public class ResumeContactTests : BunitContext
{
    private const string OwnerEmail = "Ravi@techieblog.com";
    private const string OwnerPhone = "+91 98765 43210";
    private const string OwnerLinkedIn = "https://www.linkedin.com/in/techierathore";

    /// <summary>
    /// Builds a site owner carrying every contact field, so a test asserting an absence is
    /// asserting a deliberate omission rather than absent data.
    /// </summary>
    /// <returns>A fully populated user.</returns>
    private static AppUser FullyPopulatedOwner() => new()
    {
        FirstName = "Ravi",
        LastName = "Rathore",
        EmailId = OwnerEmail,
        PhoneNumber = OwnerPhone,
        Location = "Pune, India",
        LinkedInUrl = OwnerLinkedIn
    };

    /// <summary>
    /// A user with an email address and a phone number publishes neither, and offers no
    /// mailto: or tel: target for a reader or a harvester to follow.
    /// </summary>
    [Fact]
    public void ContactPublishesNoEmailAddressOrPhoneNumber()
    {
        // Arrange, Act
        var cut = Render<ResumeContact>(parameters => parameters
            .Add(contact => contact.User, FullyPopulatedOwner()));

        // Assert
        var markup = cut.Markup;
        Assert.DoesNotContain("mailto:", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tel:", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(OwnerEmail, markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(OwnerPhone, markup, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cut.FindAll("[data-testid='contact-email']"));
        Assert.Empty(cut.FindAll("[data-testid='contact-phone']"));
        Assert.Empty(cut.FindAll("[data-testid='send-email']"));
    }

    /// <summary>
    /// LinkedIn is rendered as the primary call to action and opens in a new tab with the
    /// opener severed, since it is now the only route a reader has to the owner.
    /// </summary>
    [Fact]
    public void ContactLeadsWithTheLinkedInCallToAction()
    {
        // Arrange, Act
        var cut = Render<ResumeContact>(parameters => parameters
            .Add(contact => contact.User, FullyPopulatedOwner()));

        // Assert
        var primary = cut.Find("[data-testid='contact-linkedin']");
        Assert.Equal(OwnerLinkedIn, primary.GetAttribute("href"));
        Assert.Equal("_blank", primary.GetAttribute("target"));
        Assert.Contains("noopener", primary.GetAttribute("rel") ?? string.Empty);
        Assert.Contains("resume-contact-primary", primary.GetAttribute("class") ?? string.Empty);
        Assert.Single(cut.FindAll("[data-testid='connect-linkedin']"));
    }

    /// <summary>
    /// An owner whose only contact data is an email address and a phone number gets no Contact
    /// section at all, rather than a card whose header promises contact details and whose body
    /// is empty.
    /// </summary>
    [Fact]
    public void ContactSectionIsHiddenWhenOnlyUnpublishedFieldsAreSet()
    {
        // Arrange, Act
        var cut = Render<ResumeContact>(parameters => parameters
            .Add(contact => contact.User, new AppUser
            {
                EmailId = OwnerEmail,
                PhoneNumber = OwnerPhone
            }));

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='contact-section']"));
    }

    /// <summary>
    /// A LinkedIn URL on its own is enough to render the section, so the one published route is
    /// never suppressed by the absence of a location.
    /// </summary>
    [Fact]
    public void ContactSectionRendersForLinkedInAlone()
    {
        // Arrange, Act
        var cut = Render<ResumeContact>(parameters => parameters
            .Add(contact => contact.User, new AppUser { LinkedInUrl = OwnerLinkedIn }));

        // Assert
        Assert.Single(cut.FindAll("[data-testid='contact-section']"));
        Assert.Single(cut.FindAll("[data-testid='contact-linkedin']"));
        Assert.Empty(cut.FindAll("[data-testid='contact-location']"));
    }

    /// <summary>
    /// A location on its own still renders, so removing email and phone did not make the block
    /// depend on the owner having a LinkedIn profile.
    /// </summary>
    [Fact]
    public void ContactSectionRendersForLocationAlone()
    {
        // Arrange, Act
        var cut = Render<ResumeContact>(parameters => parameters
            .Add(contact => contact.User, new AppUser { Location = "Pune, India" }));

        // Assert
        Assert.Single(cut.FindAll("[data-testid='contact-section']"));
        Assert.Equal("Pune, India", cut.Find("[data-testid='contact-location-value']").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid='contact-linkedin']"));
    }

    /// <summary>
    /// A null user renders nothing at all, which is the state the home page is in for the
    /// moment between navigation and the owner record arriving.
    /// </summary>
    [Fact]
    public void ContactRendersNothingWithoutAUser()
    {
        // Arrange, Act
        var cut = Render<ResumeContact>(parameters => parameters
            .Add(contact => contact.User, (AppUser?)null));

        // Assert
        Assert.Empty(cut.Markup.Trim());
    }
}
