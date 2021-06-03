﻿using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Mona.SaaS.Core.Constants;
using Mona.SaaS.Core.Enumerations;
using Mona.SaaS.Core.Interfaces;
using Mona.SaaS.Core.Models;
using Mona.SaaS.Core.Models.Configuration;
using Mona.SaaS.Core.Models.Events;
using Mona.SaaS.EventProcessing.Interfaces;
using Mona.SaaS.Web.Controllers;
using Mona.SaaS.Web.Models;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;

namespace Mona.SaaS.Web.Tests
{
    public class SubscriptionControllerTests
    {
        [Fact]
        public async Task PostLiveLandingPage_WithValidSubscriptionId_ShouldPublishEvent_AndRedirectToPurchaseConfirmationUrl()
        {
            var testToken = Guid.NewGuid().ToString();
            var testUserName = "Clippy";

            var mockDeployConfig = GetOptionsSnapshotMock(GetDefaultDeploymentConfiguration());
            var mockLogger = new Mock<ILogger<SubscriptionController>>();
            var mockMpOperationService = new Mock<IMarketplaceOperationService>();
            var mockMpSubscriptionService = new Mock<IMarketplaceSubscriptionService>();
            var mockEventPublisher = new Mock<ISubscriptionEventPublisher>();
            var mockSubscriptionRepo = new Mock<ISubscriptionRepository>();
            var mockHttpContext = new Mock<HttpContext>();
            var offerConfig = GetDefaultOfferConfiguration();
            var testSubscription = CreateTestSubscription();

            SubscriptionPurchased purchasedEvent = null;

            mockEventPublisher.Setup(ep => ep.PublishEventAsync(It.IsAny<SubscriptionPurchased>())).Callback<SubscriptionPurchased>(sp => purchasedEvent = sp);
            mockHttpContext.SetupGet(hc => hc.User.Identity.IsAuthenticated).Returns(true);
            mockHttpContext.SetupGet(hc => hc.User.Claims).Returns(new Claim[] { new Claim("name", testUserName) });
            mockMpSubscriptionService.Setup(ss => ss.GetSubscriptionAsync(testSubscription.SubscriptionId)).Returns(Task.FromResult(testSubscription));

            var controllerContext = new ControllerContext(new ActionContext(mockHttpContext.Object, new RouteData(), new ControllerActionDescriptor()));

            var controllerUt = new SubscriptionController(
                mockDeployConfig.Object, offerConfig, mockLogger.Object, mockMpOperationService.Object,
                mockMpSubscriptionService.Object, mockEventPublisher.Object, mockSubscriptionRepo.Object)
            { ControllerContext = controllerContext };

            var lpModel = new LandingPageModel { SubscriptionId = testSubscription.SubscriptionId };

            var actionResult = await controllerUt.PostLiveLandingPageAsync(lpModel);

            actionResult.Should().NotBeNull();
            actionResult.Should().BeOfType<RedirectResult>();
            (actionResult as RedirectResult).Url.Should().Be(offerConfig.SubscriptionPurchaseConfirmationUrl.Replace("{subscription-id}", testSubscription.SubscriptionId));

            purchasedEvent.Should().NotBeNull();
            purchasedEvent.EventType = CoreEventTypes.SubscriptionPurchased;
            purchasedEvent.Subscription.Should().NotBeNull();
            purchasedEvent.Subscription.Should().BeEquivalentTo(testSubscription);
        }

        [Fact]
        public async Task PostLiveLandingPage_WithInvalidSubscriptionId_ShouldRespondOkWithErrorCode()
        {
            var testToken = Guid.NewGuid().ToString();
            var testUserName = "Clippy";

            var mockDeployConfig = GetOptionsSnapshotMock(GetDefaultDeploymentConfiguration());
            var mockLogger = new Mock<ILogger<SubscriptionController>>();
            var mockMpOperationService = new Mock<IMarketplaceOperationService>();
            var mockMpSubscriptionService = new Mock<IMarketplaceSubscriptionService>();
            var mockEventPublisher = new Mock<ISubscriptionEventPublisher>();
            var mockSubscriptionRepo = new Mock<ISubscriptionRepository>();
            var mockHttpContext = new Mock<HttpContext>();
            var offerConfig = GetDefaultOfferConfiguration();
            var testSubscription = CreateTestSubscription();

            mockHttpContext.SetupGet(hc => hc.User.Identity.IsAuthenticated).Returns(true);
            mockHttpContext.SetupGet(hc => hc.User.Claims).Returns(new Claim[] { new Claim("name", testUserName) });
            mockMpSubscriptionService.Setup(ss => ss.GetSubscriptionAsync(testSubscription.SubscriptionId)).Returns(Task.FromResult(null as Subscription));

            var controllerContext = new ControllerContext(new ActionContext(mockHttpContext.Object, new RouteData(), new ControllerActionDescriptor()));

            var controllerUt = new SubscriptionController(
                mockDeployConfig.Object, offerConfig, mockLogger.Object, mockMpOperationService.Object,
                mockMpSubscriptionService.Object, mockEventPublisher.Object, mockSubscriptionRepo.Object)
            { ControllerContext = controllerContext };

            var lpModel = new LandingPageModel { SubscriptionId = testSubscription.SubscriptionId };

            var actionResult = await controllerUt.PostLiveLandingPageAsync(lpModel);

            actionResult.Should().NotBeNull();
            actionResult.Should().BeOfType<ViewResult>();

            var viewResult = actionResult as ViewResult;

            viewResult.ViewName.Should().Be("Index");
            viewResult.Model.Should().NotBeNull();
            viewResult.Model.Should().BeOfType<LandingPageModel>();

            lpModel = viewResult.Model as LandingPageModel;

            lpModel.ErrorCode.Should().Be(SubscriptionController.ErrorCodes.SubscriptionActivationFailed);
            lpModel.OfferDisplayName.Should().Be(offerConfig.OfferDisplayName);
            lpModel.InTestMode.Should().Be(false);
            lpModel.OfferMarketingPageUrl.Should().Be(offerConfig.OfferMarketingPageUrl);
            lpModel.OfferMarketplaceListingUrl.Should().Be(offerConfig.OfferMarketplaceListingUrl);
            lpModel.PublisherContactPageUrl.Should().Be(offerConfig.PublisherContactPageUrl);
            lpModel.PublisherCopyrightNotice.Should().Be(offerConfig.PublisherCopyrightNotice);
            lpModel.PublisherDisplayName.Should().Be(offerConfig.PublisherDisplayName);
            lpModel.PublisherHomePageUrl.Should().Be(offerConfig.PublisherHomePageUrl);
            lpModel.PublisherPrivacyNoticePageUrl.Should().Be(offerConfig.PublisherPrivacyNoticePageUrl);
            lpModel.UserFriendlyName.Should().Be(testUserName);
        }

        [Fact]
        public async Task GetLiveLandingPage_WithNoMarketplaceToken_AndConfiguredMarketingPage_ShouldRedirectToMarketingPage()
        {
            var mockDeployConfig = GetOptionsSnapshotMock(GetDefaultDeploymentConfiguration());
            var mockLogger = new Mock<ILogger<SubscriptionController>>();
            var mockMpOperationService = new Mock<IMarketplaceOperationService>();
            var mockMpSubscriptionService = new Mock<IMarketplaceSubscriptionService>();
            var mockEventPublisher = new Mock<ISubscriptionEventPublisher>();
            var mockSubscriptionRepo = new Mock<ISubscriptionRepository>();
            var offerConfig = GetDefaultOfferConfiguration();

            var controllerUt = new SubscriptionController(
                mockDeployConfig.Object, offerConfig, mockLogger.Object, mockMpOperationService.Object,
                mockMpSubscriptionService.Object, mockEventPublisher.Object, mockSubscriptionRepo.Object);

            var actionResult = await controllerUt.GetLiveLandingPageAsync();

            actionResult.Should().NotBeNull();
            actionResult.Should().BeOfType<RedirectResult>();
            (actionResult as RedirectResult).Url.Should().Be(offerConfig.OfferMarketingPageUrl);
        }

        [Fact]
        public async Task GetLiveLandingPage_WithNoMarketplaceToken_AndNoConfiguredMarketingPage_ShouldRespondNotFound()
        {
            var mockDeployConfig = GetOptionsSnapshotMock(GetDefaultDeploymentConfiguration());
            var mockLogger = new Mock<ILogger<SubscriptionController>>();
            var mockMpOperationService = new Mock<IMarketplaceOperationService>();
            var mockMpSubscriptionService = new Mock<IMarketplaceSubscriptionService>();
            var mockEventPublisher = new Mock<ISubscriptionEventPublisher>();
            var mockSubscriptionRepo = new Mock<ISubscriptionRepository>();

            var offerConfig = GetDefaultOfferConfiguration();

            offerConfig.OfferMarketingPageUrl = null;

            var controllerUt = new SubscriptionController(
                mockDeployConfig.Object, offerConfig, mockLogger.Object, mockMpOperationService.Object,
                mockMpSubscriptionService.Object, mockEventPublisher.Object, mockSubscriptionRepo.Object);

            var actionResult = await controllerUt.GetLiveLandingPageAsync();

            actionResult.Should().NotBeNull();
            actionResult.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task GetLiveLandingPage_WithInvalidMarketplaceToken_AndAuthenticatedUser_ShouldRespondOkWithErrorCode()
        {
            var testToken = Guid.NewGuid().ToString();
            var testUserName = "Clippy";

            var mockDeployConfig = GetOptionsSnapshotMock(GetDefaultDeploymentConfiguration());
            var mockLogger = new Mock<ILogger<SubscriptionController>>();
            var mockMpOperationService = new Mock<IMarketplaceOperationService>();
            var mockMpSubscriptionService = new Mock<IMarketplaceSubscriptionService>();
            var mockEventPublisher = new Mock<ISubscriptionEventPublisher>();
            var mockSubscriptionRepo = new Mock<ISubscriptionRepository>();
            var mockHttpContext = new Mock<HttpContext>();
            var offerConfig = GetDefaultOfferConfiguration();
            var testSubscription = CreateTestSubscription();

            mockHttpContext.SetupGet(hc => hc.User.Identity.IsAuthenticated).Returns(true);
            mockHttpContext.SetupGet(hc => hc.User.Claims).Returns(new Claim[] { new Claim("name", testUserName) });
            mockMpSubscriptionService.Setup(ss => ss.ResolveSubscriptionTokenAsync(testToken)).Returns(Task.FromResult(null as Subscription));

            var controllerContext = new ControllerContext(new ActionContext(mockHttpContext.Object, new RouteData(), new ControllerActionDescriptor()));

            var controllerUt = new SubscriptionController(
                mockDeployConfig.Object, offerConfig, mockLogger.Object, mockMpOperationService.Object,
                mockMpSubscriptionService.Object, mockEventPublisher.Object, mockSubscriptionRepo.Object)
            { ControllerContext = controllerContext };

            var actionResult = await controllerUt.GetLiveLandingPageAsync(testToken);

            actionResult.Should().NotBeNull();
            actionResult.Should().BeOfType<ViewResult>();

            var viewResult = actionResult as ViewResult;

            viewResult.ViewName.Should().Be("Index");
            viewResult.Model.Should().NotBeNull();
            viewResult.Model.Should().BeOfType<LandingPageModel>();

            var lpModel = viewResult.Model as LandingPageModel;

            lpModel.ErrorCode.Should().Be(SubscriptionController.ErrorCodes.UnableToResolveMarketplaceToken);
            lpModel.OfferDisplayName.Should().Be(offerConfig.OfferDisplayName);
            lpModel.InTestMode.Should().Be(false);
            lpModel.OfferMarketingPageUrl.Should().Be(offerConfig.OfferMarketingPageUrl);
            lpModel.OfferMarketplaceListingUrl.Should().Be(offerConfig.OfferMarketplaceListingUrl);
            lpModel.PublisherContactPageUrl.Should().Be(offerConfig.PublisherContactPageUrl);
            lpModel.PublisherCopyrightNotice.Should().Be(offerConfig.PublisherCopyrightNotice);
            lpModel.PublisherDisplayName.Should().Be(offerConfig.PublisherDisplayName);
            lpModel.PublisherHomePageUrl.Should().Be(offerConfig.PublisherHomePageUrl);
            lpModel.PublisherPrivacyNoticePageUrl.Should().Be(offerConfig.PublisherPrivacyNoticePageUrl);
            lpModel.UserFriendlyName.Should().Be(testUserName);
        }

        [Fact]
        public async Task GetTestLandingPage_WithAuthenticatedUser_AndExistingSubscription_ShouldRedirectToSubscriptionConfigurationUrl()
        {
            var testToken = Guid.NewGuid().ToString();
            var testUserName = "Clippy";

            var mockDeployConfig = GetOptionsSnapshotMock(GetDefaultDeploymentConfiguration());
            var mockLogger = new Mock<ILogger<SubscriptionController>>();
            var mockMpOperationService = new Mock<IMarketplaceOperationService>();
            var mockMpSubscriptionService = new Mock<IMarketplaceSubscriptionService>();
            var mockEventPublisher = new Mock<ISubscriptionEventPublisher>();
            var mockSubscriptionRepo = new Mock<ISubscriptionRepository>();
            var mockHttpContext = new Mock<HttpContext>();
            var offerConfig = GetDefaultOfferConfiguration();
            var testSubscription = CreateTestSubscription();

            testSubscription.Status = SubscriptionStatus.Active;

            mockHttpContext.SetupGet(hc => hc.User.Identity.IsAuthenticated).Returns(true);
            mockHttpContext.SetupGet(hc => hc.User.Claims).Returns(new Claim[] { new Claim("name", testUserName) });
            mockMpSubscriptionService.Setup(ss => ss.ResolveSubscriptionTokenAsync(testToken)).Returns(Task.FromResult(testSubscription));

            var controllerContext = new ControllerContext(new ActionContext(mockHttpContext.Object, new RouteData(), new ControllerActionDescriptor()));

            var controllerUt = new SubscriptionController(
                mockDeployConfig.Object, offerConfig, mockLogger.Object, mockMpOperationService.Object,
                mockMpSubscriptionService.Object, mockEventPublisher.Object, mockSubscriptionRepo.Object)
            { ControllerContext = controllerContext };

            var actionResult = await controllerUt.GetLiveLandingPageAsync(testToken);

            actionResult.Should().NotBeNull();
            actionResult.Should().BeOfType<RedirectResult>();
            (actionResult as RedirectResult).Url.Should().Be(offerConfig.SubscriptionConfigurationUrl.Replace("{subscription-id}", testSubscription.SubscriptionId));
        }
        
        [Fact]
        public async Task GetLiveLandingPage_WithValidMarketplaceToken_AndAuthenticatedUser_AndExistingSubscription_ShouldRedirectToSubscriptionConfigurationUrl()
        {
            var testToken = Guid.NewGuid().ToString();
            var testUserName = "Clippy";

            var mockDeployConfig = GetOptionsSnapshotMock(GetDefaultDeploymentConfiguration());
            var mockLogger = new Mock<ILogger<SubscriptionController>>();
            var mockMpOperationService = new Mock<IMarketplaceOperationService>();
            var mockMpSubscriptionService = new Mock<IMarketplaceSubscriptionService>();
            var mockEventPublisher = new Mock<ISubscriptionEventPublisher>();
            var mockSubscriptionRepo = new Mock<ISubscriptionRepository>();
            var mockHttpContext = new Mock<HttpContext>();
            var offerConfig = GetDefaultOfferConfiguration();
            var testSubscription = CreateTestSubscription();

            testSubscription.Status = SubscriptionStatus.Active;

            mockHttpContext.SetupGet(hc => hc.User.Identity.IsAuthenticated).Returns(true);
            mockHttpContext.SetupGet(hc => hc.User.Claims).Returns(new Claim[] { new Claim("name", testUserName) });
            mockMpSubscriptionService.Setup(ss => ss.ResolveSubscriptionTokenAsync(testToken)).Returns(Task.FromResult(testSubscription));

            var controllerContext = new ControllerContext(new ActionContext(mockHttpContext.Object, new RouteData(), new ControllerActionDescriptor()));

            var controllerUt = new SubscriptionController(
                mockDeployConfig.Object, offerConfig, mockLogger.Object, mockMpOperationService.Object,
                mockMpSubscriptionService.Object, mockEventPublisher.Object, mockSubscriptionRepo.Object)
            { ControllerContext = controllerContext };

            var actionResult = await controllerUt.GetLiveLandingPageAsync(testToken);

            actionResult.Should().NotBeNull();
            actionResult.Should().BeOfType<RedirectResult>();
            (actionResult as RedirectResult).Url.Should().Be(offerConfig.SubscriptionConfigurationUrl.Replace("{subscription-id}", testSubscription.SubscriptionId));
        }

        [Fact]
        public async Task GetTestLandingPage_WithAuthenticatedUser_ShouldRespondOkWithTestSubscriptionDetails()
        {
            var testUserName = "Clippy";

            var mockDeployConfig = GetOptionsSnapshotMock(GetDefaultDeploymentConfiguration());
            var mockLogger = new Mock<ILogger<SubscriptionController>>();
            var mockMpOperationService = new Mock<IMarketplaceOperationService>();
            var mockMpSubscriptionService = new Mock<IMarketplaceSubscriptionService>();
            var mockEventPublisher = new Mock<ISubscriptionEventPublisher>();
            var mockSubscriptionRepo = new Mock<ISubscriptionRepository>();
            var mockHttpContext = new Mock<HttpContext>();
            var offerConfig = GetDefaultOfferConfiguration();
            var testSubscription = CreateTestSubscription();

            mockHttpContext.SetupGet(hc => hc.User.Identity.IsAuthenticated).Returns(true);
            mockHttpContext.SetupGet(hc => hc.User.Claims).Returns(new Claim[] { new Claim("name", testUserName) });
            mockHttpContext.SetupGet(hc => hc.Request.Query).Returns(new QueryCollection());

            var controllerContext = new ControllerContext(new ActionContext(mockHttpContext.Object, new RouteData(), new ControllerActionDescriptor()));

            var controllerUt = new SubscriptionController(
                mockDeployConfig.Object, offerConfig, mockLogger.Object, mockMpOperationService.Object,
                mockMpSubscriptionService.Object, mockEventPublisher.Object, mockSubscriptionRepo.Object)
            { ControllerContext = controllerContext };

            var actionResult = await controllerUt.GetTestLandingPageAsync();

            actionResult.Should().NotBeNull();
            actionResult.Should().BeOfType<ViewResult>();

            var viewResult = actionResult as ViewResult;

            viewResult.ViewName.Should().Be("Index");
            viewResult.Model.Should().NotBeNull();
            viewResult.Model.Should().BeOfType<LandingPageModel>();

            var lpModel = viewResult.Model as LandingPageModel;

            lpModel.OfferDisplayName.Should().Be(offerConfig.OfferDisplayName);
            lpModel.InTestMode.Should().Be(true);
            lpModel.BeneficiaryEmailAddress.Should().NotBeNullOrEmpty();
            lpModel.PurchaserEmailAddress.Should().NotBeNullOrEmpty();
            lpModel.OfferId.Should().NotBeNullOrEmpty();
            lpModel.OfferMarketingPageUrl.Should().Be(offerConfig.OfferMarketingPageUrl);
            lpModel.OfferMarketplaceListingUrl.Should().Be(offerConfig.OfferMarketplaceListingUrl);
            lpModel.PlanId.Should().NotBeNullOrEmpty();
            lpModel.PublisherContactPageUrl.Should().Be(offerConfig.PublisherContactPageUrl);
            lpModel.PublisherCopyrightNotice.Should().Be(offerConfig.PublisherCopyrightNotice);
            lpModel.PublisherDisplayName.Should().Be(offerConfig.PublisherDisplayName);
            lpModel.PublisherHomePageUrl.Should().Be(offerConfig.PublisherHomePageUrl);
            lpModel.PublisherPrivacyNoticePageUrl.Should().Be(offerConfig.PublisherPrivacyNoticePageUrl);
            lpModel.SubscriptionId.Should().NotBeNullOrEmpty();
            lpModel.SubscriptionName.Should().NotBeNullOrEmpty();
            lpModel.UserFriendlyName.Should().Be(testUserName);
        }

        [Fact]
        public async Task GetTestLandingPage_WithAuthenticatedUser_AndTestSubscriptionParameterOverrides_ShouldRespondOkWithTestSubscriptionDetails()
        {
            var testUserName = "Clippy";

            var mockDeployConfig = GetOptionsSnapshotMock(GetDefaultDeploymentConfiguration());
            var mockLogger = new Mock<ILogger<SubscriptionController>>();
            var mockMpOperationService = new Mock<IMarketplaceOperationService>();
            var mockMpSubscriptionService = new Mock<IMarketplaceSubscriptionService>();
            var mockEventPublisher = new Mock<ISubscriptionEventPublisher>();
            var mockSubscriptionRepo = new Mock<ISubscriptionRepository>();
            var mockHttpContext = new Mock<HttpContext>();
            var offerConfig = GetDefaultOfferConfiguration();
            var testSubscription = CreateTestSubscription();

            var subOverrideParameters = CreateTestSubscriptionQueryParameters();
            var subOverrideQueryCollection = new QueryCollection(subOverrideParameters);

            mockHttpContext.SetupGet(hc => hc.User.Identity.IsAuthenticated).Returns(true);
            mockHttpContext.SetupGet(hc => hc.User.Claims).Returns(new Claim[] { new Claim("name", testUserName) });
            mockHttpContext.SetupGet(hc => hc.Request.Query).Returns(subOverrideQueryCollection);

            var controllerContext = new ControllerContext(new ActionContext(mockHttpContext.Object, new RouteData(), new ControllerActionDescriptor()));

            var controllerUt = new SubscriptionController(
                mockDeployConfig.Object, offerConfig, mockLogger.Object, mockMpOperationService.Object,
                mockMpSubscriptionService.Object, mockEventPublisher.Object, mockSubscriptionRepo.Object)
            { ControllerContext = controllerContext };

            var actionResult = await controllerUt.GetTestLandingPageAsync();

            actionResult.Should().NotBeNull();
            actionResult.Should().BeOfType<ViewResult>();

            var viewResult = actionResult as ViewResult;

            viewResult.ViewName.Should().Be("Index");
            viewResult.Model.Should().NotBeNull();
            viewResult.Model.Should().BeOfType<LandingPageModel>();

            var lpModel = viewResult.Model as LandingPageModel;

            lpModel.OfferDisplayName.Should().Be(offerConfig.OfferDisplayName);
            lpModel.InTestMode.Should().Be(true);
            lpModel.BeneficiaryEmailAddress.Should().Be(subOverrideParameters[SubscriptionController.TestSubscriptionParameterNames.BeneficiaryUserEmail].First());
            lpModel.PurchaserEmailAddress.Should().Be(subOverrideParameters[SubscriptionController.TestSubscriptionParameterNames.PurchaserUserEmail].First());
            lpModel.OfferId.Should().Be(subOverrideParameters[SubscriptionController.TestSubscriptionParameterNames.OfferId].First());
            lpModel.OfferMarketingPageUrl.Should().Be(offerConfig.OfferMarketingPageUrl);
            lpModel.OfferMarketplaceListingUrl.Should().Be(offerConfig.OfferMarketplaceListingUrl);
            lpModel.PlanId.Should().Be(subOverrideParameters[SubscriptionController.TestSubscriptionParameterNames.PlanId].First());
            lpModel.PublisherContactPageUrl.Should().Be(offerConfig.PublisherContactPageUrl);
            lpModel.PublisherCopyrightNotice.Should().Be(offerConfig.PublisherCopyrightNotice);
            lpModel.PublisherDisplayName.Should().Be(offerConfig.PublisherDisplayName);
            lpModel.PublisherHomePageUrl.Should().Be(offerConfig.PublisherHomePageUrl);
            lpModel.PublisherPrivacyNoticePageUrl.Should().Be(offerConfig.PublisherPrivacyNoticePageUrl);
            lpModel.SubscriptionId.Should().Be(subOverrideParameters[SubscriptionController.TestSubscriptionParameterNames.SubscriptionId].First());
            lpModel.SubscriptionName.Should().Be(subOverrideParameters[SubscriptionController.TestSubscriptionParameterNames.SubscriptionName].First());
            lpModel.UserFriendlyName.Should().Be(testUserName);
        }

        [Fact]
        public async Task GetLiveLandingPage_WithValidMarketplaceToken_AndAuthenticatedUser_AndNewSubscription_ShouldRespondOkWithSubscriptionDetails()
        {
            var testToken = Guid.NewGuid().ToString();
            var testUserName = "Clippy";

            var mockDeployConfig = GetOptionsSnapshotMock(GetDefaultDeploymentConfiguration());
            var mockLogger = new Mock<ILogger<SubscriptionController>>();
            var mockMpOperationService = new Mock<IMarketplaceOperationService>();
            var mockMpSubscriptionService = new Mock<IMarketplaceSubscriptionService>();
            var mockEventPublisher = new Mock<ISubscriptionEventPublisher>();
            var mockSubscriptionRepo = new Mock<ISubscriptionRepository>();
            var mockHttpContext = new Mock<HttpContext>();
            var offerConfig = GetDefaultOfferConfiguration();
            var testSubscription = CreateTestSubscription();

            mockHttpContext.SetupGet(hc => hc.User.Identity.IsAuthenticated).Returns(true);
            mockHttpContext.SetupGet(hc => hc.User.Claims).Returns(new Claim[] { new Claim("name", testUserName) });
            mockMpSubscriptionService.Setup(ss => ss.ResolveSubscriptionTokenAsync(testToken)).Returns(Task.FromResult(testSubscription));

            var controllerContext = new ControllerContext(new ActionContext(mockHttpContext.Object, new RouteData(), new ControllerActionDescriptor()));

            var controllerUt = new SubscriptionController(
                mockDeployConfig.Object, offerConfig, mockLogger.Object, mockMpOperationService.Object,
                mockMpSubscriptionService.Object, mockEventPublisher.Object, mockSubscriptionRepo.Object)
            { ControllerContext = controllerContext };

            var actionResult = await controllerUt.GetLiveLandingPageAsync(testToken);

            actionResult.Should().NotBeNull();
            actionResult.Should().BeOfType<ViewResult>();

            var viewResult = actionResult as ViewResult;

            viewResult.ViewName.Should().Be("Index");
            viewResult.Model.Should().NotBeNull();
            viewResult.Model.Should().BeOfType<LandingPageModel>();

            var lpModel = viewResult.Model as LandingPageModel;

            lpModel.OfferDisplayName.Should().Be(offerConfig.OfferDisplayName);
            lpModel.InTestMode.Should().Be(false);
            lpModel.BeneficiaryEmailAddress.Should().Be(testSubscription.Beneficiary.UserEmail);
            lpModel.PurchaserEmailAddress.Should().Be(testSubscription.Purchaser.UserEmail);
            lpModel.IsFreeTrial.Should().Be(testSubscription.IsFreeTrial);
            lpModel.OfferId.Should().Be(testSubscription.OfferId);
            lpModel.OfferMarketingPageUrl.Should().Be(offerConfig.OfferMarketingPageUrl);
            lpModel.OfferMarketplaceListingUrl.Should().Be(offerConfig.OfferMarketplaceListingUrl);
            lpModel.PlanId.Should().Be(testSubscription.PlanId);
            lpModel.PublisherContactPageUrl.Should().Be(offerConfig.PublisherContactPageUrl);
            lpModel.PublisherCopyrightNotice.Should().Be(offerConfig.PublisherCopyrightNotice);
            lpModel.PublisherDisplayName.Should().Be(offerConfig.PublisherDisplayName);
            lpModel.PublisherHomePageUrl.Should().Be(offerConfig.PublisherHomePageUrl);
            lpModel.PublisherPrivacyNoticePageUrl.Should().Be(offerConfig.PublisherPrivacyNoticePageUrl);
            lpModel.SeatQuantity.Should().Be(testSubscription.SeatQuantity);
            lpModel.SubscriptionId.Should().Be(testSubscription.SubscriptionId);
            lpModel.SubscriptionName.Should().Be(testSubscription.SubscriptionName);
            lpModel.UserFriendlyName.Should().Be(testUserName);
        }

        [Fact]
        public async Task GetTestLandingPage_WithNoAuthenticatedUser_ShouldChallenge()
        {
            var mockDeployConfig = GetOptionsSnapshotMock(GetDefaultDeploymentConfiguration());
            var mockLogger = new Mock<ILogger<SubscriptionController>>();
            var mockMpOperationService = new Mock<IMarketplaceOperationService>();
            var mockMpSubscriptionService = new Mock<IMarketplaceSubscriptionService>();
            var mockEventPublisher = new Mock<ISubscriptionEventPublisher>();
            var mockSubscriptionRepo = new Mock<ISubscriptionRepository>();
            var mockHttpContext = new Mock<HttpContext>();
            var offerConfig = GetDefaultOfferConfiguration();

            mockHttpContext.SetupGet(hc => hc.User.Identity.IsAuthenticated).Returns(false);

            var controllerContext = new ControllerContext(new ActionContext(mockHttpContext.Object, new RouteData(), new ControllerActionDescriptor()));

            var controllerUt = new SubscriptionController(
                mockDeployConfig.Object, offerConfig, mockLogger.Object, mockMpOperationService.Object,
                mockMpSubscriptionService.Object, mockEventPublisher.Object, mockSubscriptionRepo.Object)
            { ControllerContext = controllerContext };

            var actionResult = await controllerUt.GetTestLandingPageAsync();

            actionResult.Should().NotBeNull();
            actionResult.Should().BeOfType<ChallengeResult>();
        }

        [Fact]
        public async Task GetLiveLandingPage_WithMarketplaceToken_AndNoAuthenticatedUser_ShouldChallenge()
        {
            var mockDeployConfig = GetOptionsSnapshotMock(GetDefaultDeploymentConfiguration());
            var mockLogger = new Mock<ILogger<SubscriptionController>>();
            var mockMpOperationService = new Mock<IMarketplaceOperationService>();
            var mockMpSubscriptionService = new Mock<IMarketplaceSubscriptionService>();
            var mockEventPublisher = new Mock<ISubscriptionEventPublisher>();
            var mockSubscriptionRepo = new Mock<ISubscriptionRepository>();
            var mockHttpContext = new Mock<HttpContext>();
            var offerConfig = GetDefaultOfferConfiguration();

            mockHttpContext.SetupGet(hc => hc.User.Identity.IsAuthenticated).Returns(false);

            var controllerContext = new ControllerContext(new ActionContext(mockHttpContext.Object, new RouteData(), new ControllerActionDescriptor()));

            var controllerUt = new SubscriptionController(
                mockDeployConfig.Object, offerConfig, mockLogger.Object, mockMpOperationService.Object,
                mockMpSubscriptionService.Object, mockEventPublisher.Object, mockSubscriptionRepo.Object)
            { ControllerContext = controllerContext };
             
            var actionResult = await controllerUt.GetLiveLandingPageAsync(Guid.NewGuid().ToString());

            actionResult.Should().NotBeNull();
            actionResult.Should().BeOfType<ChallengeResult>();
        }

        [Fact]
        public async Task GetTestLandingPage_WithIncompleteSetup_ShouldRedirectToSetup()
        {
            var mockDeployConfig = GetOptionsSnapshotMock(GetDefaultDeploymentConfiguration());
            var mockLogger = new Mock<ILogger<SubscriptionController>>();
            var mockMpOperationService = new Mock<IMarketplaceOperationService>();
            var mockMpSubscriptionService = new Mock<IMarketplaceSubscriptionService>();
            var mockEventPublisher = new Mock<ISubscriptionEventPublisher>();
            var mockSubscriptionRepo = new Mock<ISubscriptionRepository>();

            var offerConfig = GetDefaultOfferConfiguration();

            offerConfig.IsSetupComplete = false;

            var controllerUt = new SubscriptionController(
                mockDeployConfig.Object, offerConfig, mockLogger.Object, mockMpOperationService.Object,
                mockMpSubscriptionService.Object, mockEventPublisher.Object, mockSubscriptionRepo.Object);

            var actionResult = await controllerUt.GetTestLandingPageAsync();

            actionResult.Should().NotBeNull();
            actionResult.Should().BeOfType<RedirectToRouteResult>();
            (actionResult as RedirectToRouteResult).RouteName.Should().Be("setup");
        }

        [Fact]
        public async Task GetLiveLandingPage_WithIncompleteSetup_ShouldRedirectToSetup()
        {
            var mockDeployConfig = GetOptionsSnapshotMock(GetDefaultDeploymentConfiguration());
            var mockLogger = new Mock<ILogger<SubscriptionController>>();
            var mockMpOperationService = new Mock<IMarketplaceOperationService>();
            var mockMpSubscriptionService = new Mock<IMarketplaceSubscriptionService>();
            var mockEventPublisher = new Mock<ISubscriptionEventPublisher>();
            var mockSubscriptionRepo = new Mock<ISubscriptionRepository>();

            var offerConfig = GetDefaultOfferConfiguration();

            offerConfig.IsSetupComplete = false;

            var controllerUt = new SubscriptionController(
                mockDeployConfig.Object, offerConfig, mockLogger.Object, mockMpOperationService.Object,
                mockMpSubscriptionService.Object, mockEventPublisher.Object, mockSubscriptionRepo.Object);

            var actionResult = await controllerUt.GetLiveLandingPageAsync();

            actionResult.Should().NotBeNull();
            actionResult.Should().BeOfType<RedirectToRouteResult>();
            (actionResult as RedirectToRouteResult).RouteName.Should().Be("setup");
        }

        private Mock<IOptionsSnapshot<T>> GetOptionsSnapshotMock<T>(T options) where T : class
        {
            var mockConfig = new Mock<IOptionsSnapshot<T>>();
            mockConfig.SetupGet(s => s.Value).Returns(options);

            return mockConfig;
        }

        private DeploymentConfiguration GetDefaultDeploymentConfiguration() =>
            new DeploymentConfiguration
            {
                AppInsightsConnectionString = $"InstrumentationKey={Guid.NewGuid()}",
                AzureResourceGroupName = "mona-test-rg",
                AzureSubscriptionId = Guid.NewGuid().ToString(),
                IsTestModeEnabled = true,
                MonaVersion = "1.0",
                Name = "Mona SaaS Testing"
            };

        private OfferConfiguration GetDefaultOfferConfiguration() =>
            new OfferConfiguration
            {
                IsSetupComplete = true,
                OfferDisplayName = "Mona SaaS Testing",
                OfferMarketingPageUrl = "https://github.com/microsoft/mona-saas",
                OfferMarketplaceListingUrl = "https://azure.microsoft.com",
                PublisherContactPageUrl = "https://support.microsoft.com/contactus",
                PublisherCopyrightNotice = $"© Microsoft {DateTime.UtcNow.Year}",
                PublisherDisplayName = "Microsoft",
                PublisherHomePageUrl = "https://microsoft.com",
                PublisherPrivacyNoticePageUrl = "https://privacy.microsoft.com/en-us/privacystatement",
                SubscriptionConfigurationUrl = "https://azure.microsoft.com/mona/configure/{subscription-id}",
                SubscriptionPurchaseConfirmationUrl = "https://azure.microsoft.com/mona/purchase/{subscription-id}"
            };

        private Dictionary<string, StringValues> CreateTestSubscriptionQueryParameters() => CreateTestSubscriptionQueryParameters(Guid.NewGuid().ToString());

        private Dictionary<string, StringValues> CreateTestSubscriptionQueryParameters(string subscriptionId) =>
            new Dictionary<string, StringValues>
            {
                [SubscriptionController.TestSubscriptionParameterNames.BeneficiaryAadObjectId] = new StringValues(Guid.NewGuid().ToString()),
                [SubscriptionController.TestSubscriptionParameterNames.BeneficiaryAadTenantId] = new StringValues(Guid.NewGuid().ToString()),
                [SubscriptionController.TestSubscriptionParameterNames.BeneficiaryUserEmail] = new StringValues($"beneficiary-{Guid.NewGuid()}@microsoft.com"),
                [SubscriptionController.TestSubscriptionParameterNames.BeneficiaryUserId] = new StringValues(Guid.NewGuid().ToString()),
                [SubscriptionController.TestSubscriptionParameterNames.IsFreeTrial] = new StringValues("true"),
                [SubscriptionController.TestSubscriptionParameterNames.OfferId] = new StringValues($"Offer {Guid.NewGuid()}"),
                [SubscriptionController.TestSubscriptionParameterNames.PlanId] = new StringValues($"Plan {Guid.NewGuid()}"),
                [SubscriptionController.TestSubscriptionParameterNames.PurchaserAadObjectId] = new StringValues(Guid.NewGuid().ToString()),
                [SubscriptionController.TestSubscriptionParameterNames.PurchaserAadTenantId] = new StringValues(Guid.NewGuid().ToString()),
                [SubscriptionController.TestSubscriptionParameterNames.PurchaserUserEmail] = new StringValues($"purchaser-{Guid.NewGuid()}@microsoft.com"),
                [SubscriptionController.TestSubscriptionParameterNames.PurchaserUserId] = new StringValues(Guid.NewGuid().ToString()),
                [SubscriptionController.TestSubscriptionParameterNames.SeatQuantity] = new StringValues("40"),
                [SubscriptionController.TestSubscriptionParameterNames.SubscriptionId] = new StringValues(subscriptionId),
                [SubscriptionController.TestSubscriptionParameterNames.SubscriptionName] = new StringValues($"Subscription {subscriptionId}"),
                [SubscriptionController.TestSubscriptionParameterNames.TermEndDate] = new StringValues(DateTime.UtcNow.Date.AddMonths(2).ToString("o")),
                [SubscriptionController.TestSubscriptionParameterNames.TermStartDate] = new StringValues(DateTime.UtcNow.Date.ToString("o")),
                [SubscriptionController.TestSubscriptionParameterNames.TermUnit] = new StringValues("PT2M")
            };

        private Subscription CreateTestSubscription() => CreateTestSubscription(Guid.NewGuid().ToString());

        private Subscription CreateTestSubscription(string subscriptionId) =>
            new Subscription
            {
                IsFreeTrial = false,
                IsTest = false,
                OfferId = $"Offer {Guid.NewGuid()}",
                PlanId = $"Plan {Guid.NewGuid()}",
                SeatQuantity = 25,
                Status = SubscriptionStatus.PendingActivation,
                SubscriptionId = subscriptionId,
                SubscriptionName = $"Subscription {subscriptionId}",
                Beneficiary = new MarketplaceUser
                {
                    AadObjectId = Guid.NewGuid().ToString(),
                    AadTenantId = Guid.NewGuid().ToString(),
                    UserEmail = $"beneficiary-{subscriptionId}@microsoft.com",
                    UserId = Guid.NewGuid().ToString()
                },
                Purchaser = new MarketplaceUser
                {
                    AadObjectId = Guid.NewGuid().ToString(),
                    AadTenantId = Guid.NewGuid().ToString(),
                    UserEmail = $"purchaser-{subscriptionId}@microsoft.com",
                    UserId = Guid.NewGuid().ToString()
                },
                Term = new MarketplaceTerm
                {
                    EndDate = DateTime.UtcNow.Date.AddMonths(1),
                    StartDate = DateTime.UtcNow.Date,
                    TermUnit = "PT1M"
                }
            };
    }
}
