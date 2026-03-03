using AutoMapper;
using Figo.Diamond.Core.FigoModels;
using Figo.Diamond.Services;
using Figo.Diamond.Services.GetStaticData.Models;
using Figo.Diamond.Services.RateOnly;
using Figo.Diamond.Services.SaveOneIncToken;
using Figo.Purchase.Application.Common;
using Figo.Purchase.Application.Common.Exceptions;
using Figo.Purchase.Application.Common.Extensions;
using Figo.Purchase.Application.Common.Helpers;
using Figo.Purchase.Application.Common.Interfaces;
using Figo.Purchase.Application.Common.Models.Insurance;
using Figo.Purchase.Application.Quotes.Dtos;
using Figo.Purchase.Application.Quotes.Dtos.CompletePurchase;
using Figo.Purchase.Application.Quotes.Interfaces;
using Figo.Purchase.Application.Quotes.Responses;
using Figo.Purchase.Application.Quotes.Structs;
using Figo.Purchase.Domain.Entities;
using Figo.Purchase.Domain.Entities.PMS;
using Figo.Purchase.Domain.Entities.Social;
using Figo.Purchase.Domain.Enums;
using Figo.Purchase.Domain.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using PetCloud.Global.Infrastructure.Cache;
using PetCloud.Global.Infrastructure.Configuration;
using System.Data.SqlTypes;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using static Figo.Purchase.Application.Common.Helpers.QuoteHelper;
using MarketingChannel = Figo.Purchase.Domain.Enums.MarketingChannel;
using Modifier = Figo.Purchase.Application.Quotes.Dtos.CompletePurchase.Modifier;
using PMSDeductibles = Figo.Diamond.Core.FigoModels.PMSDeductibles;
using PMSPolicyPlans = Figo.Diamond.Core.FigoModels.PMSPolicyPlans;
using PMSReimbursements = Figo.Diamond.Core.FigoModels.PMSReimbursements;
using PolicyImage = Figo.Diamond.Core.Models.Policy.PolicyImage;
using PolicyImageRate = Figo.Diamond.Core.Models.RateOnly.PolicyImage;
using SubmitApplicationRequest = Figo.Diamond.Core.Models.Policy.SubmitApplicationRequest;

namespace Figo.Purchase.Application.Quotes.Helpers;

public class RateService : IRateService
{
    private const string DEFAULT_AGENCY_CODE = "FIGO";
    private const string TAX_COLLECTION_FEE = "Tax Collection Fee";
    private const string TAXES = "Taxes";
    private static readonly string KY = "KY";
    private readonly IPetCloudCacheProvider _cache;
    private readonly IConfiguration _configuration;
    private readonly IApplicationDbContext _context;
    private readonly IDiamondApiService _diamondApiService;
    private readonly IDiamondService _diamondService;
    private readonly IMapper _mapper;
    private readonly IPartnerSavingService _partnerSavingService;
    private readonly IPetCloudSocialDbContext _petCloudSocialDbContext;
    private readonly IPolicyDbContext _policyContext;
    private readonly IEncryptionExtension _encryption;
    private readonly IPetCloudFeatureManager _featureManager;
    private ILogger<RateService> _logger;
    private readonly string ADDRESS_CITY = "Any Town";
    private readonly string ADDRESS_HOUSE_NUMBER = "123";
    private readonly string ADDRESS_STREET_NAME = "Main";
    private readonly string AZUREPATH = "Storage:AzurePath";
    private readonly string CONNECTMEDIA = "Storage:Blobs:Containers:ConnectMedia";
    private readonly int DEFAULT_AGENCY_ID = 2;
    private readonly int DEFAULT_BREED_ID = 1;
    private readonly int DEFAULT_GENDER_FEMALE = 2;
    private readonly int DEFAULT_MINIMUM_INT = 0;
    private readonly int DEFAULT_REIMBURSEMENT_ID = 1;
    private readonly int DEFAULT_STATUS_CODE = 1;
    private readonly int DEFAULT_VERSION_ID = 1;
    private readonly string INCLUDED_VET_FEES_STATES_KEY = "IncludedVetFeesStates";
    private readonly int MONTHS = 12;
    private readonly string SKIP_AGE_FACTOR_STATES_KEY = "SkipAgeFactorStates";
    private readonly int TOTAL_PERCENTAGE = 100;
    private VersionData _versionData = new VersionData();
    private const string IMPERSONATE_CUSTOMER_ID = "ImpersonateCustomerId";
    private const string URL_PARAMETER_CALLBACK_FORMAT = "{0}/login?customerId={1}";
    private ZipCode? _zipCode;
    private StateProvince? _stateProvince;
    private List<InsuranceStateFactor>? _insuranceStateFactorList;
    private List<InsuranceWaitingPeriodByState>? _insuranceWaitingPeriodByStatesList;
    private List<InsuranceWaitingPeriodByStateEB>? _insuranceWaitingPeriodByStatesEBList;
    private List<InsuranceModifierEBByState>? _insuranceModifierEBByStatesList;
    private List<InsuranceModifierByState>? _insuranceModifierByStates;
    private List<PCBreedProduct>? _pCBreedProductsList;
    private List<Setting>? _socialSettingsList;
    private List<InsuranceModifierDiscount>? _insuranceModifierDiscountsList;
    private List<PrePackagedPlanValidOptionsByState>? _prePackagedPlanValidOptionsByStatesList;
    private List<PrepackagedPlanConfiguration>? _prepackagedPlanConfigurationsList;
    private List<PrePackagedPlanExceptionsByOrigin>? _prePackagedPlanExceptionsByOriginsList;
    private List<Deductible>? _deductiblesList;
    private List<Reimbursement>? _reimbursementList;
    private List<InsuranceStateFactorEB>? _insuranceStateFactorEBList;
    private EmployerEB? _employerEB;
    private List<EmployerEB>? _employersEBList;
    private List<InsuranceWaiveFeeEB>? _insuranceWaiveFeeEBList;
    private List<InsuranceModifierEBDefaultsByEmployer>? _insuranceModifierEBDefaultsByEmployersList;
    private List<CoverageLimitExceptionsByState>? _pmsCoverageLimits;

    private static readonly Regex NumberStreetCombinationOne = new Regex("^[A-Z]+[0-9]+$", RegexOptions.Compiled);
    private static readonly Regex NumberStreetCombinationTwo = new Regex("^[0-9]+[A-Z]+$", RegexOptions.Compiled);
    private static readonly Regex NumberAptCombination = new Regex("^[A-Za-z0-9]+$", RegexOptions.Compiled);
    private static readonly Regex HouseNumber = new Regex(@"[^0-9a-zA-Z\ ]+", RegexOptions.Compiled);

    public RateService(IApplicationDbContext context,
                       IPetCloudSocialDbContext petCloudSocialDbContext,
                       IPolicyDbContext policyContext,
                       IDiamondService diamondService,
                       IDiamondApiService diamondApiService,
                       IConfiguration configuration,
                       IPartnerSavingService partnerSavingService,
                       IPetCloudCacheProvider cache,
                       IMapper mapper,
                       IEncryptionExtension encryption,
                       IPetCloudFeatureManager featureManager,
                       ILogger<RateService> logger)
    {
        _context = context;
        _petCloudSocialDbContext = petCloudSocialDbContext;
        _policyContext = policyContext;
        _diamondService = diamondService;
        _diamondApiService = diamondApiService;
        _configuration = configuration;
        _partnerSavingService = partnerSavingService;
        _cache = cache;
        _mapper = mapper;
        _encryption = encryption;
        _featureManager = featureManager;
        _logger = logger;
    }

    public async Task SetVersionData(int versionId)
    {
        _versionData = await GetCachedVersionData(versionId);
    }

    public async Task<QuoteRateResponseDto> CreateQuoteRate(
                    QuoteRequestLegacyDto quoteRequest,
                    ZipCode zipCodeInfo,
                    bool isRate = true)
    {
        bool isCC = false;
        var response = await GetInsuranceInformation(quoteRequest.EffectiveDate, zipCodeInfo.StateAbbr, quoteRequest.IsEB, isCC, quoteRequest.Partner.PartnerGuid);

        if (response.insuranceProduct != null)
        {
            RemoveInsuranceModifiersDiscounts(response.insuranceProduct, quoteRequest);
        }

        response.petQuoteResponseList = new List<PetQuoteRateResponseDto>();

        bool multiplePetDiscount = quoteRequest.petQuotes.Count > 1 || quoteRequest.isMultiplePets;
        foreach (var petQuote in quoteRequest.petQuotes)
        {
            if (!petQuote.IsOpeningQuote && (petQuote.modifiers == null || petQuote.modifiers.Count == 0))
            {
                petQuote.IsInitialRate = true;
            }

            int petTypeId = await ValidatePetBreed(petQuote, response, zipCodeInfo);

            var petQuoteResponse = await BuildQuoteResponse(quoteRequest, petQuote, multiplePetDiscount);

            var ratePetQuoteResponse = MapQuoteData(petQuoteResponse, petQuote, quoteRequest.groupCode, petTypeId, petQuote.userSelectedInfoPlan?.PrePackagedPlanId);

            ratePetQuoteResponse.InsuranceModifiers = GetInsuranceModifiers(petQuote, response.insuranceProduct?.InsuranceModifiers.Clone());
            ratePetQuoteResponse.InsuranceModifiers = DynamicModifiers(ratePetQuoteResponse.InsuranceModifiers, petQuoteResponse.DynamicModifiers);
            ratePetQuoteResponse.InsuranceModifiers = SetIsSelectedDiscounts(petQuoteResponse.Discounts, ratePetQuoteResponse.InsuranceModifiers);
            SetIsSelectedDefaults(ratePetQuoteResponse.InsuranceModifiers, quoteRequest.IsOpenQuote, petQuote);

            response.petQuoteResponseList.Add(ratePetQuoteResponse);
        }

        response.effectiveDate = quoteRequest.effectiveDate;
        response.zipCode = quoteRequest.zipCode;
        response.stateAbrv = zipCodeInfo.StateAbbr;
        response.groupCode = await ShortPromoCode(quoteRequest.groupCode);
        response.groupCodeDscr = quoteRequest.groupCodeDscr;
        response.isMultiplePets = quoteRequest.isMultiplePets;
        response.CoverageInformation = await GetCoverageInformationDetails(response.insuranceProduct?.Id ?? 0);
        response.SiteMessages = await GetMessageSetting(zipCodeInfo.StateAbbr, quoteRequest.EffectiveDate);

        var customerMarketChannels = await _context.CustomerMarketChannels.Include(x => x.Customer).Include(x => x.MarketingChannel).Where(x => x.Customer != null && x.Customer.Username == quoteRequest.eMail).ToListAsync().ConfigureAwait(false);
        customerMarketChannels.ForEach(mc => response.MarketingChannels?.Add(new MarketingChannelDto { DisplayName = mc.MarketingChannel?.DisplayName, Id = mc.MarketChannelId, OriginId = mc.MarketingChannel?.OriginId }));

        if (response.insuranceProduct != null)
        {
            response.insuranceProduct.InsuranceModifiers.ToList().ForEach(item =>
            {
                item.OptionalBenefitsDetailsItem.ForEach(o =>
                {
                    if (o.BulletIcon != null)
                    {
                        o.BulletIcon = GetUrlImage(o.BulletIcon);
                    }
                });
            });
        }

        if (response.petQuoteResponseList != null)
        {
            response.petQuoteResponseList.ForEach(item =>
            {
                item.InsuranceModifiers?.ForEach(x =>
                {
                    x.OptionalBenefitsDetailsItem.ForEach(y =>
                    {
                        if (y.BulletIcon != null)
                        {
                            y.BulletIcon = GetUrlImage(y.BulletIcon);
                        }
                    });
                });
            });
        }

        return response;
    }

    public async Task<EmployerEB?> GetEmployerByGuid(string employerEBGuid)
    {
        if (!string.IsNullOrEmpty(employerEBGuid) && _employerEB == null)
        {
            var cacheKey = $"Figo.Static.Rate.EmployersEB.{employerEBGuid}";
            _employerEB = await _cache.GetOrCreateAsync(cacheKey, async () => await _context.EmployersEB.AsNoTracking().FirstOrDefaultAsync(e => e.GuID.ToUpper() == employerEBGuid.ToUpper()).ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);
        }

        return string.IsNullOrEmpty(employerEBGuid) ? null : _employerEB;
    }

    public async Task<IPartnerService> GetPartner(string partnerGuid)
    {
        var config = await GetPartnerConfig(partnerGuid);

        return (config?.Name) switch
        {
            Partner.Costco => new CostcoService(config, _partnerSavingService),
            Partner.GoodDog => new GoodDogService(config),
            Partner.USAA => new USAAService(config),
            Partner.UHC => new UHCService(config),
            _ => new NoPartnerService(),
        };
    }

    public async Task<QuoteRateResponseDto?> GetSelectedRate(QuoteRequestLegacyDto quoteRequest)
    {
        return !quoteRequest.IsEB ? await SelectedRate(quoteRequest) : await GetSelectedRateEB(quoteRequest);
    }

    public async Task<QuoteRateResponseDto?> GetSelectedRateEB(QuoteRequestLegacyDto quoteRequest)
    {
        Quote? quote = null;
        int? customerId = 0;

        if (quoteRequest.petQuotes == null || quoteRequest.petQuotes.Count == 0)
        {
            throw new FigoException("At least one pet must be added for quote.");
        }

        QuoteRateResponseDto response = new QuoteRateResponseDto();
        response.ebPetQuoteResponseList = new List<PetQuoteRateResponseDto>();

        ZipCode zipCodeInfo = await GetByZipcodeThrowWhenNULL(quoteRequest.zipCode ?? "");

        bool isExamFees = await IsExamFees(zipCodeInfo.StateAbbr);

        await MultiplePetCheck(quoteRequest);

        bool multiplePetDiscount = quoteRequest.petQuotes.Count > 1 || quoteRequest.isMultiplePets;

        InsuranceProductEB insuranceProduct = await GetInsuranceProductEB(zipCodeInfo, quoteRequest.EffectiveDate);

        response.insuranceProductEB = insuranceProduct;

        await WaiveFeesEB(quoteRequest.ebGuID ?? "", insuranceProduct).ConfigureAwait(false);

        if (quoteRequest.IsOpenQuote && !string.IsNullOrEmpty(quoteRequest.QuoteGuid))
        {
            Guid quoteGuid = Guid.Parse(quoteRequest.QuoteGuid);
            quote = await _context.Quotes.AsNoTracking().FirstOrDefaultAsync(x => x.GuidId == quoteGuid).ConfigureAwait(false);
        }

        foreach (var ebPetQuote in quoteRequest.petQuotes)
        {
            if (quote != null && !quote.IsPurchased)
            {
                SetPowerUpsForOpenQuote(ebPetQuote, quote.Id);
            }
            else if (ebPetQuote.modifiers == null || ebPetQuote.modifiers.Count == 0)
            {
                ebPetQuote.IsInitialRate = true;
            }

            ValidateBreedDto validateBreed = BuildValidateBreedObject(ebPetQuote, insuranceProduct.ProductFamilyID, zipCodeInfo.ZIPCode);
            BreedDto breed = await ValidatePetBreed(validateBreed);

            InsuranceProductEB clonedInsuranceProductEB = insuranceProduct.Clone();

            var petQuoteResponse = await SelectedRatePrepareEBQuoteResponseForMap(ebPetQuote, clonedInsuranceProductEB, zipCodeInfo, quoteRequest.groupCode ?? "", quoteRequest.EffectiveDate,
                multiplePetDiscount, isExamFees, breed, quoteRequest.diamondClientId, employer: quoteRequest.ebGuID);

            petQuoteResponse.InsuranceProductEB = clonedInsuranceProductEB;

            int petTypeId = (int)breed.SpeciesId;
            var ebPetQuoteResponse = MapEBQuoteData(petQuoteResponse, ebPetQuote, quoteRequest.groupCode ?? "", petTypeId, ebPetQuote.userSelectedInfoPlan?.PrePackagedPlanId);

            ebPetQuoteResponse.InsuranceModifiersEB = DynamicModifiers(petQuoteResponse.InsuranceProductEB?.InsuranceModifiersEB ?? new List<InsuranceModifierEB>(), petQuoteResponse.DynamicModifiers ?? new List<DynamicModifierDto>());
            ebPetQuoteResponse.InsuranceModifiersEB = SetIsSelectedDiscounts(petQuoteResponse.Discounts, petQuoteResponse.InsuranceProductEB?.InsuranceModifiersEB ?? new List<InsuranceModifierEB>());

            SetIsSelectedDefaults(ebPetQuoteResponse.InsuranceModifiersEB, quoteRequest.IsOpenQuote, ebPetQuote);

            response.ebPetQuoteResponseList.Add(ebPetQuoteResponse);
        }

        response.effectiveDate = quoteRequest.effectiveDate;
        response.zipCode = quoteRequest.zipCode;
        response.groupCode = quoteRequest.groupCode;
        response.groupCodeDscr = quoteRequest.groupCodeDscr;
        response.isMultiplePets = multiplePetDiscount;
        response.stateAbrv = zipCodeInfo.StateAbbr;

        response.CoverageInformation = await GetCoverageInformationDetails(response.insuranceProductEB.Id);

        response.SiteMessages = await GetMessageSetting(zipCodeInfo.StateAbbr, quoteRequest.EffectiveDate);

        response.insuranceProductEB.InsuranceModifiersEB.ToList().ForEach(item =>
        {
            item.OptionalBenefitsDetailsItem.ForEach(o =>
            {
                if (o.BulletIcon != null)
                {
                    o.BulletIcon = GetUrlImage(o.BulletIcon);
                }
            });
        });

        response.ebPetQuoteResponseList.ForEach(item =>
        {
            item.InsuranceModifiersEB?.ForEach(x =>
            {
                x.OptionalBenefitsDetailsItem.ForEach(y =>
                {
                    if (y.BulletIcon != null)
                    {
                        y.BulletIcon = GetUrlImage(y.BulletIcon);
                    }
                });
            });
        });

        return response;
    }

    public string? GetUrlImage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string url = $"{_configuration[AZUREPATH]}{_configuration[CONNECTMEDIA]}/petcloud";

        text = text.Replace("\\", "/");

        if (text.StartsWith("http"))
        {
            return text;
        }

        if (!text.StartsWith("/"))
        {
            return string.Concat(url, "/", text);
        }

        return string.Concat(url, text);
    }

    public async Task<QuoteRateResponseDto?> Rate(QuoteRequestLegacyDto quoteRequest)
    {
        return !quoteRequest.IsEB ? await QuoteRate(quoteRequest) : await QuoteRateEB(quoteRequest);
    }

    private static ICollection<InsuranceModifier> DynamicModifiers(ICollection<InsuranceModifier> insuranceModifiers, IList<DynamicModifierDto>? dynamicModifierDtos)
    {
        var productDynamicModifiers = insuranceModifiers.Where(m => m.BenefitFeeType == BenefitFeeType.Dynamic);

        if (dynamicModifierDtos == null || !dynamicModifierDtos.Any())
        {
            return insuranceModifiers;
        }

        foreach (var item in productDynamicModifiers)
        {
            item.FeeAmount = dynamicModifierDtos.FirstOrDefault(m =>
            m.PmsModifierId == item.PMSModifierId.GetValueOrDefault())?.FeeAmount;
        }

        return insuranceModifiers;
    }

    private static List<QuotePlanDtoEB> FilterRatingOptions(List<QuotePlanDtoEB>? plans, PrePackagedPlanValidOptionsByStateDto? prePackagedPlansValidOptionsByStateDto, ICollection<InsuranceProductDedReimbException>? dedReimbExceptions)
    {
        var filteredPlans = new List<QuotePlanDtoEB>();
        List<QuoteRatingOptionDtoEB>? ratingOptions = null;

        if (plans != null)
        {
            foreach (var plan in plans)
            {
                ratingOptions = new List<QuoteRatingOptionDtoEB>();

                foreach (QuoteRatingOptionDtoEB ratingOption in plan.RatingOptions)
                {
                    var exceptionPlan = dedReimbExceptions != null ? dedReimbExceptions.Where(e => e.PMSCoverageLimitId == (int)plan.Plan).ToList() : null;

                    if (prePackagedPlansValidOptionsByStateDto != null && prePackagedPlansValidOptionsByStateDto.Deductibles.Contains(ratingOption.DeductibleId) && prePackagedPlansValidOptionsByStateDto.Reimbursements.Contains(ratingOption.ReimbursementId))
                    {
                        var reimbursementPlan = new List<InsuranceProductDedReimbException>();
                        var deductibleReimbursementPlan = new List<InsuranceProductDedReimbException>();

                        bool emptyCoverage = false;

                        if (exceptionPlan != null && exceptionPlan.Count > 0)
                        {
                            reimbursementPlan = exceptionPlan.Where(e => e.Reimbursement == ratingOption.ReimbursementName?.Replace("%", "").Replace(",", "")).ToList();
                            emptyCoverage = exceptionPlan.Any(e => e.Reimbursement == null);
                        }

                        if (reimbursementPlan != null && reimbursementPlan.Count > 0)
                        {
                            deductibleReimbursementPlan = reimbursementPlan.Where(e => e.Deductible == ratingOption.DeductibleName?.Replace("$", "").Replace(",", "")).ToList();
                            emptyCoverage = reimbursementPlan.Any(e => e.Deductible == null);
                        }

                        if ((deductibleReimbursementPlan != null && deductibleReimbursementPlan.Count > 0) || emptyCoverage)
                        {
                            ratingOption.Invalid = true;
                        }

                        ratingOptions.Add(ratingOption);
                    }
                }

                string maxAnnualAmount = string.Empty;
                if (!string.IsNullOrEmpty(plan.MaxAnnual))
                {
                    maxAnnualAmount = plan.MaxAnnual.Contains(".") ? plan.MaxAnnual.Replace(".", "").Replace("k", ",00") : plan.MaxAnnual.Replace("k", ",000");
                }

                filteredPlans.Add(new QuotePlanDtoEB
                {
                    Plan = plan.Plan,
                    PlanName = plan.PlanName,
                    MaxAnnual = maxAnnualAmount,
                    RatingOptions = ratingOptions
                });
            }
        }
        return filteredPlans;
    }

    private static List<int> GetDeductibleIds(List<int> ageFactorOptionItems, List<QuoteDeductibleDto>? quoteResponseDeductibles)
    {
        var deductibleIds = new List<int>();
        QuoteDeductibleDto? deductible = null;

        for (int i = 0; i < ageFactorOptionItems.Count; i++)
        {
            if (quoteResponseDeductibles != null)
            {
                deductible = quoteResponseDeductibles.Where(r => r.DollarVal == Convert.ToDouble(ageFactorOptionItems[i])).FirstOrDefault();

                if (deductible != null)
                {
                    deductibleIds.Add(deductible.Id);
                }
            }
        }

        return deductibleIds;
    }

    private static Gender GetGender(string petSex)
    {
        Enum.TryParse(petSex, ignoreCase: true, out Gender gender);
        return gender;
    }

    private static ICollection<InsuranceModifier> GetInsuranceModifiers(RatePetQuoteDto petQuote, ICollection<InsuranceModifier>? insuranceModifiers)
    {
        if (petQuote.modifiers is null)
        {
            return insuranceModifiers ?? new List<InsuranceModifier>();
        }

        var insuranceModifiersClone = insuranceModifiers?.Select(m => m.Clone()).ToList();

        foreach (var userSelectedModifier in petQuote.modifiers)
        {
            var productModifier = insuranceModifiersClone?.Where(im => im.Id == userSelectedModifier.id).FirstOrDefault();
            if (productModifier is null)
            {
                continue;
            }

            if (productModifier.BenefitType == BenefitType.Bundle)
            {
                productModifier.IsSelected = userSelectedModifier.IsSelectedValue();
                var bundleItems = productModifier.BundleInsuranceModifiers;

                if (bundleItems != null)
                {
                    foreach (var bundle in bundleItems)
                    {
                        var selectModifier = petQuote.modifiers.Where(x => x.id == bundle.Id).FirstOrDefault();
                        if (selectModifier != null && selectModifier.id > 0)
                        {
                            bundle.IsSelected = selectModifier.IsSelectedValue();
                        }
                        else
                        {
                            bundle.IsSelected = userSelectedModifier.IsSelectedValue();
                        }
                    }
                }
            }
            else
            {
                productModifier.IsSelected = userSelectedModifier.IsSelectedValue();
            }
        }

        return insuranceModifiersClone ?? new List<InsuranceModifier>();
    }

    private static List<QuoteModifierDto> GetInsuranceProductModifiersEB(InsuranceProductEB product)
    {
        var modifiers = new List<QuoteModifierDto>();
        var modifiersGroup = product.InsuranceModifiersEB.Where(y => y.IsActive).GroupBy(x => x.InsuranceModifierTypeEBId).Select(l => l.ToList());
        QuoteModifierDto modifierTemp;
        foreach (var modifierGroup in modifiersGroup)
        {
            if (modifierGroup.Count > 0)
            {
                var modifierGroupOrder = modifierGroup.OrderBy(x => x.OrderNumber).ToList();

                modifierTemp = new QuoteModifierDto()
                {
                    DisplayName = modifierGroupOrder.First().InsuranceModifierTypeEB?.DisplayName,
                    ModifierDetails = new List<QuoteModifierDetailDto>(),
                    ModifierType = modifierGroupOrder.First().InsuranceModifierTypeEBId
                };
                foreach (var modifier in modifierGroupOrder)
                {
                    if (modifier.IsActive)
                    {
                        modifierTemp.ModifierDetails.Add(new QuoteModifierDetailDto()
                        {
                            Id = modifier.Id,
                            TitleText = modifier.TitleText,
                            InputText = modifier.InputText,
                            IsSelected = modifier.IsSelected ?? false,
                            PMSModifierId = modifier.PMSModifierId,
                            BundleInsuranceModifiers = modifier.BundleInsuranceModifiersEB ?? new List<InsuranceModifierEB>(),
                            InformationText = modifier.InformationText,
                            QuestionType = modifier.QuestionType,
                            FeeAmount = modifier.FeeAmount,
                            IsVisible = modifier.IsVisible,
                            BenefitParentId = modifier.BenefitParentId,
                            BenefitType = modifier.BenefitType,
                            CoverageLimitId = modifier.CoverageLimitId
                        });
                    }
                }
                modifiers.Add(modifierTemp);
            }
        }
        return modifiers;
    }

    private static decimal GetPremium(Dictionary<Tuple<int, int, int>, decimal> premiums, int planId, int deductibleId, int reimbursementId)
    {
        Tuple<int, int, int> tuple = new Tuple<int, int, int>(planId, deductibleId, reimbursementId);
        premiums.TryGetValue(tuple, out decimal premium);
        return premium;
    }

    private static decimal GetWellnessIhcPrice(List<Diamond.Core.Models.RateOnly.Coverage> coverages)
    {
        var wellness = coverages.FirstOrDefault(c => c.CoverageCodeID == (int)CoverageCode.Wellness);
        var wellnessWaitingPeriod_14Day = coverages.FirstOrDefault(c => c.CoverageLimitId == (int)CoverageLimit.WellnessWaitingPeriod_14Day);

        if (wellness != null && wellnessWaitingPeriod_14Day != null)
        {
            return wellness.FullTermPremium != null ? (decimal)wellness.FullTermPremium : 0;
        }

        return 0;
    }

    private static void HandleAddModifier(RatePetQuoteDto petQuote, InsuranceModifierEB? insuranceProductModifier, bool? selected)
    {
        if (insuranceProductModifier is null)
        {
            return;
        }

        if (petQuote.modifiers is null)
        {
            petQuote.modifiers = new List<UserSelectedModifiersDto>();
        }

        var filteredModifier = petQuote.modifiers.Where(x => x.id == insuranceProductModifier.Id).FirstOrDefault();

        if (filteredModifier != null && filteredModifier.id > 0)
        {
            petQuote.modifiers.Remove(filteredModifier);
        }

        petQuote.modifiers.Add(new UserSelectedModifiersDto() { id = insuranceProductModifier.Id, isSelected = selected });
    }

    private static void HandleParentModifier(RatePetQuoteDto petQuote, ICollection<InsuranceModifierEB> insuranceModifiers, Diamond.Core.CoverageCode modifier, bool? isSelected = false)
    {
        var insuranceProductModifier = insuranceModifiers.Where(x => x.PMSModifierId == (int)modifier).FirstOrDefault();
        HandleAddModifier(petQuote, insuranceProductModifier, isSelected);
    }

    private static PetQuoteRateResponseDto MapQuoteData(QuoteResponseDto petQuoteResponseDto, RatePetQuoteDto petQuote, string? promoCode, int petTypeId, int? prePackagedPlanId)
    {
        return new PetQuoteRateResponseDto
        {
            petQuoteId = petQuote.id,
            cloudOrderId = petQuote.cloudOrderId,
            promoCode = promoCode,
            petName = petQuote.petName,
            petType = (PetTypes)(petTypeId - 1),
            modifiers = petQuote.modifiers,
            annualPremium = petQuoteResponseDto.AnnualPremium != null ? (decimal)petQuoteResponseDto.AnnualPremium : 0,
            breedId = petQuoteResponseDto.BreedId,
            breedName = petQuoteResponseDto.BreedName,
            Deductible = petQuoteResponseDto.Deductible,
            deductibleName = petQuoteResponseDto.DeductibleName,
            Deductibles = petQuoteResponseDto.Deductibles,
            Discounts = petQuoteResponseDto.Discounts,
            gender = petQuoteResponseDto.Gender,
            genderName = petQuoteResponseDto.Gender.ToString(),
            monthlyPremium = petQuoteResponseDto.MonthlyPremium != null ? (decimal)petQuoteResponseDto.MonthlyPremium : 0,
            petAgeId = petQuoteResponseDto.PetAgeId,
            petAgeName = petQuoteResponseDto.PetAgeName,
            Plan = petQuoteResponseDto.Plan,
            planName = petQuoteResponseDto.PlanName,
            Plans = petQuoteResponseDto.PlansEB,
            Reimbursement = petQuoteResponseDto.Reimbursement,
            ReimbursementName = petQuoteResponseDto.ReimbursementName,
            Reimbursements = petQuoteResponseDto.Reimbursements,
            annualTaxes = petQuoteResponseDto.AnnualTaxes,
            monthlyTaxes = petQuoteResponseDto.MonthlyTaxes,
            QuoteId = Guid.NewGuid().ToString(),
            PrePackagedPlans = petQuoteResponseDto.PrePackagedPlans,
            PrePackagedPlanId = prePackagedPlanId ?? petQuoteResponseDto.PrepackagedPlanId,
            PrepackagedPlanDisclaimer = petQuoteResponseDto.PrepackagedPlanDisclaimer
        };
    }

    private static void ReadPropertiesRecursive(object request)
    {
        string name = string.Empty;
        string propertyValue = string.Empty;
        Dictionary<string, string> invalidProperties = new Dictionary<string, string>();

        try
        {
            if (request is null)
            {
                return;
            }

            var type = request.GetType();

            foreach (PropertyInfo property in type.GetProperties())
            {
                if (!property.CanRead)
                {
                    continue;
                }

                name = property.Name;
                if (property.PropertyType == typeof(string))
                {
                    name = property.Name;
                    propertyValue = property.GetValue(request)?.ToString() ?? "";

                    if (propertyValue != null)
                    {
                        string filterText = propertyValue.ToString();

                        var specialCharacterSet = new Regex("[<>`]");

                        if (specialCharacterSet.IsMatch(filterText) && !string.IsNullOrEmpty(filterText))
                        {
                            string[] charactersToDelete = { "<", ">", "`" };

                            foreach (string c in charactersToDelete)
                            {
                                filterText = filterText.Replace(c, "");
                            }

                            property.SetValue(request, filterText);
                        }
                    }
                }
                else if (property.PropertyType.IsClass
                    && property.PropertyType != typeof(byte[]) && property.PropertyType != typeof(byte)
                    && property.PropertyType != typeof(string) && property.PropertyType != typeof(string)
                    && property.PropertyType != typeof(bool) && property.PropertyType != typeof(bool)
                    && property.PropertyType != typeof(int) && property.PropertyType != typeof(int)
                    && property.PropertyType != typeof(DateTime) && property.PropertyType != typeof(DateTime)
                    && property.PropertyType != typeof(Guid) && property.PropertyType != typeof(Guid))
                {
                    ReadPropertiesRecursive(property.GetValue(request) ?? "");
                }
            }
        }
        catch (Exception ex)
        {
            invalidProperties.Add(name, propertyValue);
        }
    }

    private static ICollection<InsuranceModifier> SetIsSelectedDiscounts(List<DiscountDto>? discounts, ICollection<InsuranceModifier> insuranceModifiers)
    {
        if (discounts is null || discounts.Count == 0)
        {
            return insuranceModifiers;
        }

        foreach (var insuranceModifier in insuranceModifiers.Where(w => w.InsuranceModifierTypeId == (int)ModifierTypeEnum.DISCOUNT))
        {
            if (discounts.Exists(w => w.Id == insuranceModifier.PMSModifierId.GetValueOrDefault()))
            {
                insuranceModifier.IsSelected = true;
                insuranceModifier.InsuranceModifierDiscount = discounts.Where(w => w.Id == insuranceModifier.PMSModifierId.GetValueOrDefault()).First().InsuranceModifierDiscount;
            }
        }

        return insuranceModifiers;
    }

    private static void SetQuoteRiskLevel(PolicyImage diamondQuote)
    {
        diamondQuote.LOB.RiskLevel = new Diamond.Core.Models.Policy.RiskLevel();
        diamondQuote.LOB.RiskLevel.Locations = new List<Diamond.Core.Models.Policy.Location>();
        var loc = new Diamond.Core.Models.Policy.Location
        {
            Address = diamondQuote.PolicyHolder.Address
        };
        diamondQuote.LOB.RiskLevel.Locations.Add(loc);
    }

    private static void ValidateBreed(ValidateBreedDto validateBreedDto, BreedDto breed)
    {
        if (breed != null && breed.PetCloudBreedId > 0)
        {
            return;
        }

        if (validateBreedDto.IsValidateBreed)
        {
            throw new FigoException("Breed conflict. Contact support.");
        }

        string message = $"Breed missing mapping - BreedId:{validateBreedDto.BreedId} - ProductFamily:{validateBreedDto.ProductFamily} - ZipCode:{validateBreedDto.ZipCode} - BreedName:{validateBreedDto.BreedName}";

        if (validateBreedDto.isRateCC)
        {
            throw new FigoException("Pet breed is not configured for the insurance product.");
        }
        else
        {
            throw new FigoException($"{message} - Invalid Pet breed.");
        }
    }

    private static void ValidatePetQuote(QuoteRequestLegacyDto quoteRequestDto)
    {
        if (quoteRequestDto.petQuotes == null || quoteRequestDto.petQuotes.Count == 0)
        {
            throw new FigoException("At least one pet must be added for quote.");
        }
    }

    private async Task AddModifier(int modifierDetailId, PolicyImage image, bool enableModifier = true)
    {
        var mod = await GetOrAddModifier(modifierDetailId, image.LOB.PolicyLevel.Modifiers, image.VersionId);

        if (mod != null)
        {
            mod.CheckboxSelected = enableModifier;
        }
    }

    private async Task AddOrUpdateModifiers(List<QuoteModifierDto> modifiers, PolicyImage image)
    {
        Diamond.Core.Models.Policy.Modifier? mod = null;

        if (modifiers != null)
        {
            foreach (var modifier in modifiers)
            {
                if (modifier.ModifierDetails != null)
                {
                    foreach (var detail in modifier.ModifierDetails)
                    {
                        if (detail.PMSModifierId != null)
                        {
                            if (modifier.ModifierType == (int)ModifierTypeEnum.COVERAGE)
                            {
                                if (detail.BenefitType == BenefitType.Bundle)
                                {
                                    foreach (var item in detail.BundleInsuranceModifiers)
                                    {
                                        if ((item?.IsSelected != null && item.IsSelected == true) || item?.BenefitFeeType == BenefitFeeType.Dynamic)
                                        {
                                            if (item.CoverageLimitId != null)
                                            {
                                                image.LOB.PolicyLevel.Coverages.Add(new Diamond.Core.Models.Policy.Coverage
                                                {
                                                    CoverageCodeID = item.PMSModifierId == null ? (int)detail.PMSModifierId : (int)item.PMSModifierId,
                                                    Checkbox = item.IsSelected,
                                                    CoverageLimitId = (int)item.CoverageLimitId,
                                                    CoverageNum = new Diamond.Core.Models.Policy.CoverageNum { InternalValue = Guid.NewGuid().ToString() }
                                                });
                                            }
                                            else
                                            {
                                                image.LOB.PolicyLevel.Coverages.Add(new Diamond.Core.Models.Policy.Coverage
                                                {
                                                    CoverageCodeID = item.PMSModifierId == null ? (int)detail.PMSModifierId : (int)item.PMSModifierId,
                                                    Checkbox = item.IsSelected,
                                                    CoverageNum = new Diamond.Core.Models.Policy.CoverageNum { InternalValue = Guid.NewGuid().ToString() }
                                                });
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    image.LOB.PolicyLevel.Coverages.Add(new Diamond.Core.Models.Policy.Coverage
                                    {
                                        CoverageCodeID = (int)detail.PMSModifierId,
                                        Checkbox = detail.IsSelected,
                                        CoverageNum = new Diamond.Core.Models.Policy.CoverageNum { InternalValue = Guid.NewGuid().ToString() }
                                    });
                                }
                            }
                            else
                            {
                                mod = await GetOrAddModifier((int)detail.PMSModifierId, image.LOB.PolicyLevel.Modifiers, image.VersionId);
                                if (mod != null)
                                {
                                    if (detail.QuestionType == (int)InsuranceModifierQuestionTypeEnum.YesNoQuestion)
                                    {
                                        await SetModifierOption(mod, image.VersionId, detail.IsSelected ? "Yes" : "No");
                                    }
                                    else
                                    {
                                        mod.CheckboxSelected = detail.IsSelected;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private async Task AgeFactorFilterEB(QuoteResponseDto response, int petTypeId, int petAge, InsuranceProductEB insuranceProduct, bool applyDefaultAgeFactor)
    {
        if (response.PlansEB == null)
        {
            return;
        }

        var cacheKey = $"Figo.Static.Rate.AgeFactorsEB.{petAge}.{petTypeId}.{insuranceProduct.Id}";
        var ageFactorsEB = await _cache.GetOrCreateAsync(cacheKey, async () => await _context.AgeFactorsEB.AsNoTracking().Where(ageFactor => ageFactor.PetAge == petAge && ageFactor.PetType == petTypeId && ageFactor.InsuranceProductEBId == insuranceProduct.Id).ToListAsync().ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);

        var ageFactorDeductible = ageFactorsEB.FirstOrDefault(ageFactor => ageFactor.FactorType == (int)RateFactor.Deductible);

        var ageFactorReimbursement = ageFactorsEB.FirstOrDefault(ageFactor => ageFactor.FactorType == (int)RateFactor.Reimbursement);

        if (ageFactorDeductible != null && ageFactorReimbursement != null)
        {
            var exceptions = insuranceProduct.InsuranceProductDedReimbExceptionsEB.Where(x => x.PMSCoverageLimitId != null && x.PMSCoverageLimitId != 0).ToList();
            var deductibleIds = new List<int>();
            var reimbursementIds = new List<int>();

            QuoteDeductibleDto? deductible = null;
            double deductibleValue = 0;
            foreach (string items in ageFactorDeductible.OptionItems)
            {
                deductibleValue = double.Parse(items, CultureInfo.InvariantCulture);
                deductible = response.Deductibles?.FirstOrDefault(r => r.DollarVal == deductibleValue);
                if (deductible != null)
                {
                    deductibleIds.Add(deductible.Id);
                }
            }

            QuoteReimbursementDto? reimb = null;
            double reimbVal = 0;
            foreach (string items in ageFactorReimbursement.OptionItems)
            {
                reimbVal = (TOTAL_PERCENTAGE - double.Parse(items)) / TOTAL_PERCENTAGE;
                reimb = response.Reimbursements?.FirstOrDefault(r => r.PercentVal == reimbVal);
                if (reimb != null)
                {
                    reimbursementIds.Add(reimb.Id);
                }
            }

            var filteredPlans = new List<QuotePlanDtoEB>();
            List<QuoteRatingOptionDtoEB>? ratingOptions = null;
            foreach (var plan in response.PlansEB)
            {
                ratingOptions = new List<QuoteRatingOptionDtoEB>();

                // For each RatingOption, make sure the deductible/reimbursement combination exists in the options we retrieved earlier from AgeFactorEB table.
                // If it doesn't, then leave it out.
                foreach (QuoteRatingOptionDtoEB ratingOption in plan.RatingOptions)
                {
                    //The coverage plan is gotten from the exception list.
                    var exceptionPlan = exceptions.Where(e => e.PMSCoverageLimitId == (int)plan.Plan).ToList();

                    //If the coverage plan exists inside the valid combination of AgeFactorTable then proceed
                    if (deductibleIds.Contains(ratingOption.DeductibleId) && reimbursementIds.Contains(ratingOption.ReimbursementId))
                    {
                        var reimbursementPlan = new List<InsuranceProductDedReimbExceptionEB>();
                        var deductibleReimbursementPlan = new List<InsuranceProductDedReimbExceptionEB>();

                        bool emptyCoverage = false;

                        //If the plan exists as a exception then search by reimbursement.
                        if (exceptionPlan != null && exceptionPlan.Count > 0)
                        {
                            reimbursementPlan = exceptionPlan.Where(e => e.Reimbursement == ratingOption.ReimbursementName?.Replace("%", "").Replace(",", "")).ToList();
                            emptyCoverage = exceptionPlan.Any(e => e.Reimbursement == null);
                        }

                        //If the reimbursement plan exists as a exception then search by deductible.
                        if (reimbursementPlan != null && reimbursementPlan.Count > 0)
                        {
                            deductibleReimbursementPlan = reimbursementPlan.Where(e => e.Deductible == ratingOption.DeductibleName?.Replace("$", "").Replace(",", "")).ToList();
                            emptyCoverage = reimbursementPlan.Any(e => e.Deductible == null);
                        }

                        if ((deductibleReimbursementPlan != null && deductibleReimbursementPlan.Count > 0) || emptyCoverage)
                        {
                            ratingOption.Invalid = true;
                        }

                        ratingOptions.Add(ratingOption);
                    }
                }

                filteredPlans.Add(new QuotePlanDtoEB
                {
                    Plan = plan.Plan,
                    PlanName = plan.PlanName,
                    MaxAnnual = plan.MaxAnnual,
                    RatingOptions = ratingOptions
                });
            }

            response.PlansEB = filteredPlans;

            if (applyDefaultAgeFactor)
            {
                if (ageFactorDeductible?.OptionItems.Count > 0 && ageFactorDeductible.DefaultOption > 0 && ageFactorDeductible.DefaultOption <= ageFactorDeductible.OptionItems.Count)
                {
                    var defaultIntDeductible = int.Parse(ageFactorDeductible.OptionItems[ageFactorDeductible.DefaultOption.Value - 1]);
                    var defaultDeductible = response.Deductibles?.FirstOrDefault(d => d.DollarVal == defaultIntDeductible);
                    response.Deductible = defaultDeductible != null ? (PMSDeductibles)defaultDeductible.Id : response.Deductible;
                    response.DeductibleName = EnumUtil.GetEnumDescription(response.Deductible);
                }

                if (ageFactorReimbursement?.OptionItems.Count > 0 && ageFactorReimbursement.DefaultOption > 0 && ageFactorReimbursement.DefaultOption <= ageFactorReimbursement.OptionItems.Count)
                {
                    var defaultIntReimbursement = int.Parse(ageFactorReimbursement.OptionItems[ageFactorReimbursement.DefaultOption.Value - 1]);
                    var defaultReimbursements = response.Reimbursements?.FirstOrDefault(r => r.Description != null && r.Description.Contains((100 - defaultIntReimbursement).ToString(), StringComparison.OrdinalIgnoreCase));
                    response.Reimbursement = defaultReimbursements != null ? (PMSReimbursements)defaultReimbursements.Id : response.Reimbursement;
                    response.ReimbursementName = EnumUtil.GetEnumDescription(response.Reimbursement);
                }

                var plans = response.PlansEB.FirstOrDefault(p => p.Plan == response.Plan);
                if (plans != null)
                {
                    var option = plans.RatingOptions.
                        Where(o => o.DeductibleId == (int)response.Deductible &&
                        o.ReimbursementId == (int)response.Reimbursement).FirstOrDefault();
                    if (option != null)
                    {
                        response.AnnualPremium = (double)option.AnnualPremium;
                        response.MonthlyPremium = (double)option.MonthlyPremium;
                    }
                }
            }
        }
    }

    private async Task ApplyDynamicModifiers(InsuranceProductEB insuranceProduct, QuoteDto quote, bool multiplePetDiscount, QuoteResponseDto petQuoteResponse)
    {
        var dynamicModifiers = insuranceProduct.InsuranceModifiersEB
            .FirstOrDefault(m => m.BenefitFeeType == BenefitFeeType.Dynamic && (m.IsSelected != null ? !(bool)m.IsSelected : true));

        if (dynamicModifiers == null)
        {
            return;
        }

        var insuranceProductCopy = insuranceProduct.Clone();

        var bundleItems = insuranceProductCopy.InsuranceModifiersEB
            .FirstOrDefault(m => m.Id == dynamicModifiers.Id)?.BundleInsuranceModifiersEB;

        bundleItems?.ForEach(b => b.IsSelected = true);

        QuoteResponseDto dynamicRate = await GetPolicyRateOnlyEB(quote, insuranceProductCopy, multiplePetDiscount);

        petQuoteResponse.DynamicModifiers = dynamicRate.DynamicModifiers;
    }

    private async Task ApplyPromocodes(List<QuoteModifierDto> modifiers, PolicyImage diamondQuote, PetCloudProductFamily productFamily, string promoCodes)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(promoCodes))
        {
            return;
        }

        string[] allCodes = promoCodes.Split(',');

        if (allCodes == null)
        {
            return;
        }

        if (!ValidPMSModifiers(modifiers.First().ModifierDetails, productFamily))
        {
            return;
        }

        foreach (string code in allCodes)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            Diamond.Core.Models.Agency.Data.LookupPolicyPromo.LookupPolicyPromoResponse? promoCodeResponse = await _diamondService.LookupPolicyPromoCode(CheckForMaskedCode(code));

            Diamond.Core.Models.Rate.PolicyPromo? promoCode = promoCodeResponse != null ? promoCodeResponse.LookupPolicyPromo : null;

            if (promoCode != null &&
                ((diamondQuote.LOB.PolicyLevel.PolicyPromos.Find(x => x.PolicyPromoTypeId > 0 && x.PolicyPromoTypeId == promoCode.PolicyPromoTypeId) == null) ||
                (diamondQuote.LOB.PolicyLevel.PolicyPromos.Find(x => x.PolicyPromoTypeId == 0 && x.NoMatchPromoCode == promoCode.NoMatchPromoCode) == null)))
            {
                diamondQuote.LOB.PolicyLevel.PolicyPromos.Add(new Diamond.Core.Models.Policy.PolicyPromo
                {
                    NoMatchPromoCode = promoCode.NoMatchPromoCode,
                    PolicyPromoTypeId = promoCode.PolicyPromoTypeId,
                    DetailStatusCode = promoCode.DetailStatusCode
                });
            }
        }
    }

    private async Task<QuoteResponseDto> BuildQuoteResponse(
                 QuoteRequestLegacyDto quoteRequest,
         RatePetQuoteDto petQuote,
         bool multiplePetDiscount
         )
    {
        QuoteDto quote = await BuilQuoteDto(quoteRequest, petQuote);

        var insuranceProduct = await GetInsuranceProductByIsEBAndStateFactor(new InsuranceProductRequestDto
        {
            BuildBundles = true,
            EffectiveDate = quoteRequest.EffectiveDate,
            IsEB = quoteRequest.IsEB,
            StateAbbr = quote.ZipCodeInfo?.State ?? "",
            RemoveModifiers = true,
            PartnerGuid = quoteRequest.Partner.PartnerGuid
        });

        BreedDto breed = await ValidatePetBreed(petQuote, quoteRequest, insuranceProduct?.ProductFamilyID ?? PetCloudProductFamily.None);

        quote.PetBreedId = breed.DiamondBreedId;
        await SetInsuranceProductDefaults(quote, insuranceProduct);

        var insuranceProductMap = _mapper.Map<InsuranceProduct, InsuranceProductEB>(insuranceProduct ?? new InsuranceProduct());

        SetInsuranceModifiers(petQuote, insuranceProductMap);

        RemoveInsuranceModifiersDiscounts(insuranceProductMap, quoteRequest);

        int petAgeYears = QuoteHelper.LoadAges().FirstOrDefault(a => a.Description == petQuote.petAge)?.Years ?? 0;
        await GetPrePackagedPlanValidOptionsByAge(petAgeYears).ConfigureAwait(false);
        await SetAndGetReimbusementsAndDeductibles().ConfigureAwait(false);

        QuoteResponseDto petQuoteResponse = await GetPolicyRate(quote, insuranceProductMap, multiplePetDiscount);

        await ApplyDynamicModifiers(insuranceProductMap, quote, multiplePetDiscount, petQuoteResponse);

        (await GetPartner(quoteRequest.Partner.PartnerGuid))
            .CalculateSaving(insuranceProduct?.InsuranceModifiers ?? new List<InsuranceModifier>(), petQuoteResponse.PlansEB ?? new List<QuotePlanDtoEB>(), multiplePetDiscount);

        await ValidRatingOptionsFilter(petQuoteResponse, insuranceProduct, petAgeYears, partnerGuid: quoteRequest.Partner.PartnerGuid).ConfigureAwait(false);

        petQuoteResponse.PrePackagedPlans = await GetPrePackagedPlansByAge(petAgeYears, insuranceProduct?.Id ?? 0, insuranceProduct?.SelectedStateFactor?.StateId ?? 0, quoteRequest.Partner.PartnerGuid);

        SetPlanIdToPrepackagedPlans(petQuoteResponse.PlansEB, petQuoteResponse.PrePackagedPlans);

        SetPrepackagedPlanDefaults(petQuoteResponse, petQuote);

        SetPetAgeInformation(petAgeYears, petQuoteResponse);

        SetBreedInformation(breed, petQuoteResponse);
        petQuoteResponse.Gender = quote.Gender;

        petQuoteResponse.PrepackagedPlanDisclaimer = await GetPrepackagedPlanDisclaimerByOrigin(insuranceProduct?.SelectedStateFactor?.StateId ?? 0, insuranceProduct?.Id ?? 0, petAgeYears, false, quoteRequest.Partner.PartnerGuid, null);

        return petQuoteResponse;
    }

    private async Task<QuoteResponseDto> BuildQuoteResponseSelectedRate(
                 QuoteRequestLegacyDto quoteRequest,
         RatePetQuoteDto petQuote,
         bool multiplePetDiscount
         )
    {
        QuoteDto quote = await BuilQuoteDto(quoteRequest, petQuote);

        var insuranceProduct = await GetInsuranceProductByIsEBAndStateFactor(new InsuranceProductRequestDto
        {
            BuildBundles = true,
            EffectiveDate = quoteRequest.EffectiveDate,
            IsEB = quoteRequest.IsEB,
            StateAbbr = quote.ZipCodeInfo?.State ?? "",
            RemoveModifiers = true,
            PartnerGuid = quoteRequest.Partner.PartnerGuid
        });

        BreedDto breed = await ValidatePetBreed(petQuote, quoteRequest, insuranceProduct?.ProductFamilyID ?? PetCloudProductFamily.None);

        quote.PetBreedId = breed.DiamondBreedId;
        await SetInsuranceProductDefaults(quote, insuranceProduct);

        var insuranceProductMap = _mapper.Map<InsuranceProduct, InsuranceProductEB>(insuranceProduct ?? new InsuranceProduct());

        SetInsuranceModifiers(petQuote, insuranceProductMap);

        RemoveInsuranceModifiersDiscounts(insuranceProductMap, quoteRequest);

        QuoteResponseDto petQuoteResponse = await GetPolicyRate(quote, insuranceProductMap, multiplePetDiscount);

        await ApplyDynamicModifiers(insuranceProductMap, quote, multiplePetDiscount, petQuoteResponse);

        (await GetPartner(quoteRequest.Partner.PartnerGuid))
            .CalculateSaving(insuranceProduct?.InsuranceModifiers ?? new List<InsuranceModifier>(), petQuoteResponse.PlansEB ?? new List<QuotePlanDtoEB>(), multiplePetDiscount);

        int petAgeYears = QuoteHelper.LoadAges().FirstOrDefault(a => a.Description == petQuote.petAge)?.Years ?? 0;

        await ValidRatingOptionsFilter(petQuoteResponse, insuranceProduct, petAgeYears, partnerGuid: quoteRequest.Partner.PartnerGuid).ConfigureAwait(false);

        petQuoteResponse.PrePackagedPlans = await GetPrePackagedPlansByAge(petAgeYears, insuranceProduct?.Id ?? 0, insuranceProduct?.SelectedStateFactor?.StateId ?? 0, quoteRequest.Partner.PartnerGuid);

        SetPlanIdToPrepackagedPlans(petQuoteResponse.PlansEB, petQuoteResponse.PrePackagedPlans);

        SetPrepackagedPlanDefaultsForGetSelectedRate(petQuoteResponse, petQuote);

        SetPetAgeInformation(petAgeYears, petQuoteResponse);

        SetBreedInformation(breed, petQuoteResponse);
        petQuoteResponse.Gender = quote.Gender;

        petQuoteResponse.PrepackagedPlanDisclaimer = await GetPrepackagedPlanDisclaimerByOrigin(insuranceProduct?.SelectedStateFactor?.StateId ?? 0, insuranceProduct?.Id ?? 0, petAgeYears, false, quoteRequest.Partner.PartnerGuid, null);

        return petQuoteResponse;
    }

    private static ValidateBreedDto BuildValidateBreedObject(RatePetQuoteDto ratePetQuote, PetCloudProductFamily productFamily, string zipCode)
    {
        return new ValidateBreedDto
        {
            BreedId = ratePetQuote.petBreedId,
            BreedName = ratePetQuote.petBreed,
            ProductFamily = productFamily,
            ZipCode = zipCode,
            isRateCC = ratePetQuote.isRateCC
        };
    }

    private async Task<QuoteDto> BuilQuoteDto(QuoteRequestLegacyDto quoteRequest, RatePetQuoteDto petQuote)
    {
        var zipCodeInfo = await GetZipCodeInfo(quoteRequest.zipCode ?? "");

        return new QuoteDto
        {
            Plan = petQuote.plan,
            Deductible = petQuote.deductible,
            Reimbursement = petQuote.reimbursement,
            PetName = petQuote.petName,
            PetBirthDate = QuoteHelper.GetDateOfBirth(petQuote.petAge ?? ""),
            Gender = GetGender(petQuote.petSex ?? ""),
            EffectiveDate = quoteRequest.EffectiveDate,
            PromoCode = await GetPromoCode(quoteRequest),
            ZipCodeInfo = zipCodeInfo,
            IsExamFees = await IsExamFees(zipCodeInfo.State),
            CustomerEmail = string.Empty,
            CustomerName = string.Empty
        };
    }

    private double CalculateReimbursementRate(string coInsuranceRate)
    {
        if (String.IsNullOrWhiteSpace(coInsuranceRate))
        {
            return DEFAULT_MINIMUM_INT;
        }

        coInsuranceRate = coInsuranceRate.Replace("%", String.Empty).Trim();
        Double.TryParse(coInsuranceRate, out double reimbursementRate);

        if (reimbursementRate == DEFAULT_MINIMUM_INT)
        {
            reimbursementRate = DEFAULT_REIMBURSEMENT_ID;
        }
        else
        {
            reimbursementRate = 100 - reimbursementRate;
            reimbursementRate /= 100;
        }

        return reimbursementRate;
    }

    private string CheckForMaskedCode(string promoCode)
    {
        if (string.IsNullOrEmpty(promoCode))
        {
            return promoCode;
        }

        promoCode = promoCode.Trim();
        if (IsMaskValid(promoCode))
        {
            promoCode = promoCode.Substring(3, promoCode.Length - 6);
        }
        return promoCode;
    }

    private static void ConfigureModifiers(RatePetQuoteDto petQuote, ICollection<InsuranceModifierEB> insuranceModifiers)
    {
        UserSelectedModifiersDto userSelectedModifier;

        foreach (var modifierSelectedByUser in petQuote.modifiers)
        {
            var modifierFromInsuranceProduct = insuranceModifiers.Where(im => im.Id == modifierSelectedByUser.id).FirstOrDefault();

            if (modifierFromInsuranceProduct is null)
            {
                continue;
            }

            userSelectedModifier = modifierSelectedByUser;
            modifierFromInsuranceProduct.IsSelected = userSelectedModifier.IsSelectedValue();

            if (modifierFromInsuranceProduct.BenefitType == BenefitType.Bundle)
            {
                if (modifierFromInsuranceProduct.BundleInsuranceModifiersEB != null)
                {
                    foreach (var bundle in modifierFromInsuranceProduct.BundleInsuranceModifiersEB)
                    {
                        var userSelectedModifiersFromPet = petQuote.modifiers.Where(x => x.id == bundle.Id).FirstOrDefault();

                        if (userSelectedModifiersFromPet != null && userSelectedModifiersFromPet.id > 0)
                        {
                            userSelectedModifier = userSelectedModifiersFromPet;
                        }
                        else
                        {
                            userSelectedModifier = modifierSelectedByUser;
                        }

                        bundle.IsSelected = userSelectedModifier.IsSelectedValue();
                    }
                }
            }
        }
    }

    private static QuotePlanDtoEB CreatePlanEB(Dictionary<Tuple<int, int, int>, decimal> premiums, InsuranceProductPlanEB plan, List<QuoteDeductibleDto> deductibles, List<QuoteReimbursementDto> reimbursements)
    {
        var quotePlan = new QuotePlanDtoEB
        {
            Plan = (PMSPolicyPlans)plan.PMSCoverageLimitId,
            PlanName = plan.Name,
            MaxAnnual = plan.MaxAnnual,
            FilteredByState = plan.FilteredByState
        };

        quotePlan.RatingOptions = new List<QuoteRatingOptionDtoEB>();
        decimal amount, deductibleValue, reimbursementValue;
        foreach (var deductible in deductibles)
        {
            foreach (var reimbursement in reimbursements)
            {
                amount = GetPremium(premiums, plan.PMSCoverageLimitId, deductible?.Id ?? 0, reimbursement.Id);
                var ratingOption = new QuoteRatingOptionDtoEB
                {
                    DeductibleId = deductible?.Id ?? 0,
                    DeductibleName = deductible?.Description,
                    DeductiblValue = Decimal.TryParse(deductible?.Description?.Replace("$", "").Replace(",", ""), out deductibleValue) ? deductibleValue : 0,
                    ReimbursementId = reimbursement.Id,
                    ReimbursementName = reimbursement.Description,
                    ReimbursementValue = Decimal.TryParse(reimbursement.Description?.Replace("%", ""), out reimbursementValue) ? reimbursementValue : 0,
                    AnnualPremium = amount,
                    MonthlyPremium = amount / 12,
                    PlanId = (int)quotePlan.Plan,
                    AnnualBenefit = plan.MaxAnnual
                };
                quotePlan.RatingOptions.Add(ratingOption);
            }
        }
        return quotePlan;
    }

    private async Task<PolicyImage> CreatePolicyDiamondEB(QuoteDto quote, InsuranceProductEB product, bool multiplePetDiscount)
    {
        ReadPropertiesRecursive(quote);
        ReadPropertiesRecursive(product);
        var diamondQuote = new PolicyImage();

        diamondQuote.AdditionalPolicyHolders = new List<object>();
        diamondQuote.TransactionRemark = string.Empty;
        diamondQuote.PolicyNumber = string.Empty;
        diamondQuote.PolicyNumberSuffix = string.Empty;
        diamondQuote.AdditionalTransactionInformation = string.Empty;
        diamondQuote.PackageParts = new List<object>();
        diamondQuote.CurrentPackagePartIndex = -1;

        string state = quote?.ZipCodeInfo?.State ?? "";
        diamondQuote.VersionId = quote?.VersionId ?? 0;

        if (quote != null)
        {
            var systemData = await GetCachedSystemData();
            StateData? diamondState = systemData.States.FirstOrDefault(x => x.State == state);

            GetQuoteRatingVersion(quote, diamondQuote, systemData);
            GetQuoteUnderwritingVersionId(quote, diamondQuote, systemData);
            GetQuoteAddFormsVersion(quote, diamondQuote, systemData);

            diamondQuote.EffectiveDate = new Diamond.Core.Models.Policy.EffectiveDate { DateTime = quote.EffectiveDate };
            diamondQuote.ExpirationDate = new Diamond.Core.Models.Policy.ExpirationDate { DateTime = quote.EffectiveDate.AddYears(1) };
            diamondQuote.TransactionDate = new Diamond.Core.Models.Policy.TransactionDate { DateTime = diamondQuote.EffectiveDate.DateTime };
            diamondQuote.TransactionEffectiveDate = new Diamond.Core.Models.Policy.TransactionEffectiveDate { DateTime = diamondQuote.EffectiveDate.DateTime };
            diamondQuote.TransactionExpirationDate = new Diamond.Core.Models.Policy.TransactionExpirationDate { DateTime = diamondQuote.ExpirationDate.DateTime };
            diamondQuote.TransactionTypeId = (int)Diamond.Core.TransType.NewPolicy;
            diamondQuote.ReceivedDate = new Diamond.Core.Models.Policy.ReceivedDate { DateTime = DateTime.Today };

            await GetQuoteAnnualTerm(diamondQuote);

            diamondQuote.GuaranteedRatePeriodEffectiveDate = new Diamond.Core.Models.Policy.GuaranteedRatePeriodEffectiveDate { DateTime = diamondQuote.EffectiveDate.DateTime };
            diamondQuote.GuaranteedRatePeriodExpirationDate = new Diamond.Core.Models.Policy.GuaranteedRatePeriodExpirationDate { DateTime = diamondQuote.ExpirationDate.DateTime };

            var agencyId = await GetDefaultAgencyId(quote.PMSCompanyId, diamondState?.StateId ?? 0, quote.PMSLobId, diamondQuote, quote.PromoCode, systemData);
            diamondQuote.Agency = await GetAgencyData(agencyId);
            diamondQuote.AgencyId = agencyId;

            diamondQuote.LOB = new Diamond.Core.Models.Policy.LOB
            {
                PolicyLevel = new Diamond.Core.Models.Policy.PolicyLevel
                {
                    Coverages = new List<Diamond.Core.Models.Policy.Coverage>(),
                    PolicyPromos = new List<Diamond.Core.Models.Policy.PolicyPromo>()
                }
            };

            diamondQuote.Policy = new Diamond.Core.Models.Policy.Policy();
            diamondQuote.Policy.Account = new Diamond.Core.Models.Policy.Account();

            diamondQuote.Policy.Account.BillMethodId = (int)BillMethod.DirectBill;
            diamondQuote.BillToId = (int)Diamond.Core.BillTo.Insured;
            await SetQuoteCoveragesEB(quote, diamondQuote, product);
            await SetQuoteModifiersEB(quote.PromoCode ?? "", diamondQuote, product, multiplePetDiscount);
            SetQuotePolicyHolderAddress(quote, diamondQuote, diamondState);
            diamondQuote = await SetQuoteTaxExceptions(quote, diamondQuote, product, diamondState?.StateId ?? 0);
            SetQuoteRiskLevel(diamondQuote);
        }

        return diamondQuote;
    }

    #region CompletePurchase Methods

    public async Task<string?> GetPromoCode(string? partnerGuid)
    {
        string? promoCode = null;
        if (!string.IsNullOrEmpty(partnerGuid))
        {
            var service = await GetPartner(partnerGuid);
            promoCode = service.GetAvailablePromoCode();
        }

        return promoCode;
    }

    public async Task SetModifiers(PurchaseInfoDto? purchaseInfo, PolicyImageRate pImage, bool hasEnrollFee, List<ModifierType>? modifierTypes, List<ModifierOption>? modifierOptions)
    {
        bool multiPet = purchaseInfo?.IncludeMultiPetDiscount ?? false;

        // Vet Modifier
        Diamond.Core.Models.RateOnly.Modifier? vetModifier = AddModifier((int)ModifierTypeId.SalesforceVetId, pImage.LOB.PolicyLevel.Modifiers, modifierTypes ?? [], null, "0010a00001WKi7Z") ?? new();

        var modifierOption = modifierOptions?.FirstOrDefault(x =>
                            x.ModifierGroupId == vetModifier?.ModifierGroupId &&
                            x.ModifierLevelId == vetModifier?.ModifierLevelId &&
                            x.ModifierTypeId == vetModifier?.ModifierTypeId &&
                            string.Equals(x.Description, "0010a00001WKi7Z", StringComparison.OrdinalIgnoreCase));

        vetModifier.ModifierOptionId = modifierOption != null ? modifierOption.ModifierOptionId : (double)1;

        // Partner Modifier
        var partnerService = await GetPartner(purchaseInfo?.Partner.PartnerGuid ?? string.Empty);
        if (partnerService.Partner == Partner.Costco && purchaseInfo?.Partner.IsFeeWaived == true)
        {
            AddModifier((int)ModifierTypeId.WaiveEnrollmentFee, pImage.LOB.PolicyLevel.Modifiers, modifierTypes ?? [], true, purchaseInfo?.Partner.MembershipId);
        }

        if (!pImage.LOB.PolicyLevel.Modifiers.Any(x => x.ModifierTypeId == (int)ModifierTypeId.WaiveEnrollmentFee) && !hasEnrollFee)
        {
            AddModifier((int)ModifierTypeId.WaiveEnrollmentFee, pImage.LOB.PolicyLevel.Modifiers, modifierTypes ?? [], true);
        }

        AddModifier((int)ModifierTypeId.MembershipId, pImage.LOB.PolicyLevel.Modifiers, modifierTypes ?? [], null, purchaseInfo?.Partner.MembershipId);

        // Callback Modifier
        string encryptedCustomerId = _encryption.EncryptInt(IMPERSONATE_CUSTOMER_ID, purchaseInfo?.CloudCustomerId ?? 0);
        string baseUrl = _configuration["ServiceURLNewPetcloud"] ?? string.Empty;
        var url = string.Format(URL_PARAMETER_CALLBACK_FORMAT, baseUrl, encryptedCustomerId);

        AddModifier((int)ModifierTypeId.PetCloudPortalLink, pImage.LOB.PolicyLevel.Modifiers, modifierTypes ?? [], null, url);

        // Employee Modifiers
        var employeeIdModifier = AddModifier((int)ModifierTypeId.EmployeeID, pImage.LOB.PolicyLevel.Modifiers, modifierTypes ?? [], null, purchaseInfo?.EmployeeId);
        var employeeGuidModifier = AddModifier((int)ModifierTypeId.EmployerID, pImage.LOB.PolicyLevel.Modifiers, modifierTypes ?? [], null, purchaseInfo?.EmployerGuid);
        var employer = GetEmployerByGuid(purchaseInfo?.EmployerGuid ?? string.Empty);

        if (employer is not null)
        {
            foreach (var fee in _context.InsuranceWaiveFeesEB.Where(x => x.EmployerEBId == employer.Id))
            {
                AddModifier(fee.ModifierTypeId, pImage.LOB.PolicyLevel.Modifiers, modifierTypes ?? [], true);
            }
        }

        // CX Agent Modifier
        var agentModifier = AddModifier((int)ModifierTypeId.CXAgentID, pImage.LOB.PolicyLevel.Modifiers, modifierTypes ?? [], null, purchaseInfo?.AgentId);
    }

    public async Task SetSelectedCoveragePlan(PetInsuranceQuoteModel insurancePetQuote, Diamond.Core.Models.RateOnly.Coverage petCoverage, List<CoverageLimitVersion> coverageLimits, List<InsuranceProductPlan> productPlans)
    {
        if (productPlans == null)
        {
            petCoverage.CoverageLimitId = (int)CoverageLimit.Preferred;
            return;
        }

        var matchingPlan = productPlans.FirstOrDefault(p =>
            string.Equals(p?.Name, insurancePetQuote.SelectedPlan, StringComparison.CurrentCultureIgnoreCase));

        int? coverageLimitId = matchingPlan?.PMSCoverageLimitId;

        var coverageLimit = coverageLimitId.HasValue
            ? coverageLimits.FirstOrDefault(cl => cl.CoverageLimitId == coverageLimitId.Value)
            : coverageLimits.FirstOrDefault(cl =>
                string.Equals(cl.Description, insurancePetQuote.SelectedPlan, StringComparison.CurrentCultureIgnoreCase));

        petCoverage.CoverageLimitId = coverageLimit?.CoverageLimitId ?? (int)CoverageLimit.Preferred;
    }

    public List<Modifier> GetInsuranceProductModifiers(InsuranceProduct? product)
    {
        if (product?.InsuranceModifiers is null)
        {
            return [];
        }

        return product.InsuranceModifiers
            .Where(mod => mod.IsActive)
            .GroupBy(mod => mod.InsuranceModifierTypeId)
            .Select(group =>
            {
                var orderedGroup = group.OrderBy(mod => mod.OrderNumber).ToArray();
                var header = orderedGroup[0];

                return new Modifier
                {
                    DisplayName = header.InsuranceModifierType?.DisplayName,
                    ModifierType = header.InsuranceModifierTypeId,
                    ModifierDetails = orderedGroup
                        .Select(mod => new ModifierDetail
                        {
                            Id = mod.Id,
                            TitleText = mod.TitleText,
                            InputText = mod.InputText,
                            IsSelected = mod.IsSelected ?? false,
                            PMSModifierId = mod.PMSModifierId,
                            BundleInsuranceModifiers = GetBundleInsuranceModifiers(mod?.BundleInsuranceModifiers),
                            InformationText = mod?.InformationText,
                            QuestionType = mod?.QuestionType,
                            FeeAmount = mod?.FeeAmount,
                            IsVisible = mod?.IsVisible ?? false,
                            BenefitParentId = mod?.BenefitParentId,
                            BenefitType = mod?.BenefitType,
                            CoverageLimitId = mod?.CoverageLimitId
                        })
                        .ToList()
                };
            })
            .ToList();
    }

    private static List<InsuranceModifierEB> GetBundleInsuranceModifiers(List<InsuranceModifier>? insuranceModifiers)
    {
        if (insuranceModifiers == null)
        {
            return [];
        }

        return insuranceModifiers.ConvertAll(item => new InsuranceModifierEB
        {
            Id = item.Id,
            AppText = item.AppText,
            IsActive = item.IsActive,
            IsVisible = item.IsVisible,
            CreatedOn = item.CreatedOn,
            FeeAmount = item.FeeAmount,
            InputText = item.InputText ?? string.Empty,
            TitleText = item.TitleText,
            IsSelected = item.IsSelected,
            BenefitType = item.BenefitType,
            OrderNumber = item.OrderNumber,
            QuestionType = item.QuestionType,
            PMSModifierId = item.PMSModifierId,
            BenefitFeeType = item.BenefitFeeType,
            InformationText = item.InformationText,
            CoverageLimitId = item.CoverageLimitId,
            BenefitParentId = item.BenefitParentId,
            MaximumAnnualBenefit = item.MaximumAnnualBenefit,
            InsuranceProductEBId = item.InsuranceProductId,
            InsuranceModifierTypeEBId = item.InsuranceModifierTypeId
        });
    }

    public async Task ApplyPromocodes(List<Modifier>? modifiers, PolicyImageRate image, PetCloudProductFamily? productFamily, string? promoCodes)
    {
        if (modifiers?.Count <= 0 || string.IsNullOrWhiteSpace(promoCodes))
        {
            return;
        }

        string[] allCodes = promoCodes.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                      .Select(code => code.Trim())
                                      .ToArray();
        if (allCodes.Length == 0)
        {
            return;
        }

        var modifierDetails = modifiers?[0].ModifierDetails;

        var modifierGroupCode = modifierDetails?.FirstOrDefault(x => x.PMSModifierId == (int)ModifierTypeId.GroupCode);
        var modifierAffinityStrat40049999 = modifierDetails?.FirstOrDefault(x => x.PMSModifierId == (int)ModifierTypeId.AffinityStrat40049999);
        var modifierAffinityStrat50000 = modifierDetails?.FirstOrDefault(x => x.PMSModifierId == (int)ModifierTypeId.AffinityStrat50000);

        bool isGroupCode = modifierGroupCode != null;
        bool validAffinityStrat40049999 = modifierAffinityStrat40049999 != null;
        bool validAffinityStrat50000 = modifierAffinityStrat50000 != null && productFamily == PetCloudProductFamily.FPI;

        if (!(isGroupCode || validAffinityStrat40049999 || validAffinityStrat50000))
        {
            return;
        }

        // Fire off all lookup requests concurrently.
        var lookupTasks = allCodes.Select(code => _diamondService.LookupPolicyPromoCode(code).AsTask()).ToList();
        var responses = await Task.WhenAll(lookupTasks);

        foreach (var response in responses)
        {
            var promoCode = response.LookupPolicyPromo;
            if (promoCode == null)
            {
                continue;
            }

            bool greaterThanZero = image.LOB!.PolicyLevel.PolicyPromos
                .Find(x => x.PolicyPromoTypeId > 0 && x.PolicyPromoTypeId == promoCode.PolicyPromoTypeId) == null;
            bool equalToZero = image.LOB.PolicyLevel.PolicyPromos
                .Find(x => x.PolicyPromoTypeId == 0 && x.NoMatchPromoCode == promoCode.NoMatchPromoCode) == null;

            if (greaterThanZero || equalToZero)
            {
                image.LOB.PolicyLevel.PolicyPromos.Add(_mapper.Map<Diamond.Core.Models.RateOnly.PolicyPromo>(promoCode));
            }
        }
    }

    public void SetDiscounts(List<Modifier>? modifiers, PolicyImageRate returnImage, bool multiPetDiscount, PetCloudProductFamily? productFamily, List<ModifierType> modifierTypes)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }

        var modifierDetails = modifiers[0].ModifierDetails;
        if (modifierDetails == null)
        {
            return;
        }

        var lob = returnImage.LOB;
        var policyModifiers = lob?.PolicyLevel?.Modifiers;

        var modifierDetailMultiplePetDiscount = modifierDetails.FirstOrDefault(x => x.PMSModifierId == (int)ModifierTypeId.MultiplePetDiscount);
        var modifierDetailMultiPet = modifierDetails.FirstOrDefault(x => x.PMSModifierId == (int)ModifierTypeId.MultiPet);
        var modifierDetailMultiPolicyDiscountOverride = modifierDetails.FirstOrDefault(x => x.PMSModifierId == (int)ModifierTypeId.MultiPolicyDiscountOverride);
        var modifierDetailAffinityStrat50000 = modifierDetails.FirstOrDefault(x => x.PMSModifierId == (int)ModifierTypeId.AffinityStrat50000);

        if (modifierDetailAffinityStrat50000 != null && productFamily == PetCloudProductFamily.FPI)
        {
            AddModifier((int)ModifierTypeId.AffinityStrat50000, policyModifiers, modifierTypes, true);
        }

        if (modifierDetailMultiPolicyDiscountOverride != null && multiPetDiscount)
        {
            AddModifier((int)ModifierTypeId.MultiPolicyDiscountOverride, policyModifiers, modifierTypes, true);
        }

        if (modifierDetailMultiplePetDiscount != null && productFamily == PetCloudProductFamily.IHC)
        {
            AddModifier((int)ModifierTypeId.MultiplePetDiscount, policyModifiers, modifierTypes, multiPetDiscount);
        }

        if (modifierDetailMultiPet != null && productFamily == PetCloudProductFamily.FPI)
        {
            AddModifier((int)ModifierTypeId.MultiPet, policyModifiers, modifierTypes, multiPetDiscount);
        }
    }

    public Diamond.Core.Models.RateOnly.Modifier? AddModifier(int modifierTypeId, List<Diamond.Core.Models.RateOnly.Modifier>? modifiers, List<ModifierType> modifierTypes, bool? isEnabled = null, string? modifierOptionDescription = null)
    {
        if (modifiers is null)
        {
            return null;
        }

        var modifierType = modifierTypes.FirstOrDefault(mt => mt.ModifierTypeId == modifierTypeId);
        if (modifierType == null)
        {
            return null;
        }

        var modifier = modifiers.FirstOrDefault(m => m.ModifierTypeId == modifierTypeId);
        if (modifier == null)
        {
            modifier = new() { IsNew = true, InternalFlags = 15 };
            modifiers.Add(modifier);
        }

        modifier.DetailStatusCode = (int)StatusCode.Active;
        modifier.ModifierTypeId = modifierTypeId;
        modifier.ParentModifierTypeId = modifierType.ParentModifierTypeId;
        modifier.ModifierLevelId = modifierType.ModifierLevelId;
        modifier.ModifierGroupId = modifierType.ModifierGroupId;
        modifier.CheckboxSelected = isEnabled;
        modifier.ModifierOptionDescription = string.IsNullOrEmpty(modifierOptionDescription) ? null : modifierOptionDescription;

        return modifier;
    }

    public void AddOrUpdateModifiers(List<Modifier> modifiers, PolicyImageRate image, List<SelectedModifier>? userSelectedModifiers, List<ModifierType> modifierTypes, List<ModifierOption> modifierOptions)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }

        foreach (var modifier in modifiers.Where(m => m?.ModifierDetails is not null))
        {
            foreach (var detail in modifier.ModifierDetails!)
            {
                if (detail.PMSModifierId == null)
                {
                    continue;
                }

                if (modifier.ModifierType == (int)ModifierTypeEnum.COVERAGE)
                {
                    var userSelectionDetail = userSelectedModifiers?.FirstOrDefault(x => x.Id == detail.Id);

                    if (detail.BenefitType == BenefitType.Bundle)
                    {
                        if (detail.BundleInsuranceModifiers == null)
                        {
                            continue;
                        }

                        foreach (var bundleItem in detail.BundleInsuranceModifiers)
                        {
                            var userSelectionBundle = userSelectedModifiers?.FirstOrDefault(x => x.Id == bundleItem.Id);
                            bool isSelected = userSelectionBundle?.IsSelected ?? false;

                            if (!isSelected)
                            {
                                continue;
                            }

                            int coverageCodeId = bundleItem.PMSModifierId == null
                                ? (int)detail.PMSModifierId
                                : (int)bundleItem.PMSModifierId;

                            var coverage = new Diamond.Core.Models.RateOnly.Coverage
                            {
                                CoverageCodeID = coverageCodeId,
                                Checkbox = isSelected,
                                IsNew = true,
                                InternalFlags = 15
                            };

                            if (bundleItem.CoverageLimitId != null)
                            {
                                coverage.CoverageLimitId = (int)bundleItem.CoverageLimitId;
                            }

                            image.LOB?.PolicyLevel?.Coverages.Add(coverage);
                        }
                    }
                    else
                    {
                        bool isSelected = userSelectionDetail?.IsSelected ?? false;

                        var coverage = new Diamond.Core.Models.RateOnly.Coverage
                        {
                            CoverageCodeID = (int)detail.PMSModifierId,
                            Checkbox = isSelected,
                            IsNew = true,
                            InternalFlags = 15
                        };

                        image.LOB?.PolicyLevel?.Coverages.Add(coverage);
                    }
                }
                else
                {
                    var updatedModifier = AddModifier((int)detail.PMSModifierId, image.LOB?.PolicyLevel?.Modifiers, modifierTypes);

                    if (updatedModifier == null)
                    {
                        continue;
                    }

                    if (detail.QuestionType == (int)InsuranceModifierQuestionTypeEnum.YesNoQuestion)
                    {
                        var optionString = detail.IsSelected ? "Yes" : "No";
                        updatedModifier.ModifierOptionDescription = optionString;

                        var modifierOption = modifierOptions.FirstOrDefault(x =>
                            x.ModifierGroupId == updatedModifier.ModifierGroupId &&
                            x.ModifierLevelId == updatedModifier.ModifierLevelId &&
                            x.ModifierTypeId == updatedModifier.ModifierTypeId &&
                            string.Equals(x.Description, optionString, StringComparison.OrdinalIgnoreCase));

                        updatedModifier.ModifierOptionId = modifierOption?.ModifierOptionId ?? 1;
                    }
                    else
                    {
                        updatedModifier.CheckboxSelected = detail.IsSelected;
                    }
                }
            }
        }
    }

    public void SetPolicyCoverages(PolicyImageRate returnImage, List<InsurancePolicyDefaultCoverage>? coverages)
    {
        var newCoverages = coverages?
            .Where(c => c != null)
            .Select(c => new Diamond.Core.Models.RateOnly.Coverage
            {
                CoverageCodeID = c.InsurancePolicyCoverageType?.PMSCoverageCodeId ?? 0,
                Checkbox = c.IsChecked,
                IsNew = true,
                InternalFlags = 15
            })
            .ToList() ?? [];

        returnImage.LOB!.PolicyLevel.Coverages.AddRange(newCoverages);
    }

    public DateTime DiamondDate(DateTime d, bool includeTime)
    {
        return DateTime.Compare(d, DateTime.MinValue) == 0 || DateTime.Compare(d, new DateTime(599264352000000000L)) == 0 || DateTime.Compare(d, SqlDateTime.MinValue.Value) < 0 || DateTime.Compare(d, SqlDateTime.MaxValue.Value) > 0
            ? new DateTime(567709344000000000L)
            : includeTime ? d : d.Date;
    }

    public (string, string) SetSpecificAddressInfo(string lineAddress, Diamond.Core.Models.RateOnly.Address diamAddr)
    {
        // Flag to indicate when an apartment number might span two parts.
        bool isAptTwoParts = false;
        string prevPartType = string.Empty;
        string streetName = string.Empty;

        foreach (string part in lineAddress.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string partUpper = part.ToUpperInvariant();

            // Determine if the current part is a house number.
            bool isHouseNumber = (NumberStreetCombinationOne.IsMatch(partUpper) ||
                                  NumberStreetCombinationTwo.IsMatch(partUpper) ||
                                  Information.IsNumeric(part)) &&
                                  string.IsNullOrEmpty(diamAddr.HouseNumber);

            // Determine if the current part looks like an apartment label.
            bool isAptLabel = (partUpper == "APT" ||
                               partUpper == "UNIT" ||
                               partUpper.StartsWith("#") ||
                               partUpper == "APART" ||
                               partUpper == "APTMT" ||
                               partUpper == "APARTMENT" ||
                               partUpper == "AP");

            // If we detect an apartment label (that isn’t a direct "#..." value),
            // then we might be processing a two-part apartment number.
            if (isAptLabel)
            {
                isAptTwoParts = !partUpper.StartsWith("#");
            }

            if (isHouseNumber)
            {
                diamAddr.HouseNumber = part;
                prevPartType = "HouseNumber";
                continue;
            }

            if (isAptLabel || isAptTwoParts)
            {
                string cleanedPart = partUpper.Replace("#", "");
                bool validNumber = NumberAptCombination.IsMatch(cleanedPart);

                if (validNumber)
                {
                    if (string.IsNullOrEmpty(diamAddr.ApartmentNumber))
                    {
                        diamAddr.ApartmentNumber = part;
                    }
                    else
                    {
                        diamAddr.ApartmentNumber += (isAptTwoParts ? " " : "") + part;
                    }
                }
                else
                {
                    isAptLabel = false;
                    isAptTwoParts = false;
                }
                prevPartType = "AptNum";
                continue;
            }

            streetName = string.IsNullOrEmpty(streetName)
                ? part
                : $"{streetName} {part}";
            if (!isAptLabel && isAptTwoParts)
            {
                isAptTwoParts = false;
            }
        }

        if (string.IsNullOrEmpty(diamAddr.HouseNumber) && !string.IsNullOrEmpty(streetName))
        {
            string firstWord = streetName.Split(' ')[0];
            diamAddr.HouseNumber = HouseNumber.Replace(firstWord, "");
            streetName = streetName.Replace(diamAddr.HouseNumber, "");
        }

        return (streetName, prevPartType);
    }

    public async Task<Diamond.Core.Models.RateOnly.Agency> GetDefaultAgency(int companyId, int stateId, int lobId, string? groupPartnerCode, int? companyStateLobId)
    {
        Diamond.Core.Models.RateOnly.Agency agency = await GetAgency(groupPartnerCode, companyStateLobId);

        if (ToInt(agency.AgencyId) > -1)
        {
            return agency;
        }

        agency = new() { AgencyId = DEFAULT_AGENCY_ID };

        var agencyDataList = await _diamondService.GetAgencyData(stateId, companyId, lobId);

        if (agencyDataList == null || agencyDataList.Count == 0)
        {
            return agency;
        }

        var agencyData = _mapper.Map<Diamond.Core.Models.RateOnly.Agency>(agencyDataList.FirstOrDefault(x => x.Code == DEFAULT_AGENCY_CODE)) ?? agency;

        DateTime diamondMinDate = new DateTime(1800, 1, 1, 0, 0, 0);
        agencyData.SuspendDate = agencyData.SuspendDate <= diamondMinDate ? null : agencyData.SuspendDate;
        agencyData.CloseDate = agencyData.CloseDate <= diamondMinDate ? null : agencyData.CloseDate;
        agencyData.CompanyStateLobAgencyCloseDate = agencyData.CompanyStateLobAgencyCloseDate <= diamondMinDate ? null : agencyData.CompanyStateLobAgencyCloseDate;
        agencyData.CompanyStateLobAgencySuspendDate = agencyData.CompanyStateLobAgencySuspendDate <= diamondMinDate ? null : agencyData.CompanyStateLobAgencySuspendDate;

        return agencyData;
    }

    private async Task<Diamond.Core.Models.RateOnly.Agency> GetAgency(string? groupPartnerCode, int? companyStateLobId)
    {
        Diamond.Core.Models.RateOnly.Agency agency = new() { AgencyId = -1 };

        if (companyStateLobId == null || string.IsNullOrEmpty(groupPartnerCode))
        {
            return agency;
        }

        var policyPromoCategories = await _diamondService.LoadPolicyPromoConfig(groupPartnerCode);

        if (policyPromoCategories.Count == 0)
        {
            return agency;
        }

        var policyPromoCategoryConfigs = policyPromoCategories.Where(x =>
               (x.CompanyStateLobId == companyStateLobId || x.CompanyStateLobId == 0)
               && !string.IsNullOrEmpty(x.AdditionalInfo)
               && !string.IsNullOrWhiteSpace(x.AdditionalInfo));

        if (policyPromoCategoryConfigs?.Any() != true)
        {
            return agency;
        }

        var agencyByCode = await _diamondService.GetAgencyByCodeRateOnly(policyPromoCategoryConfigs?.FirstOrDefault()?.AdditionalInfo ?? string.Empty);

        if (agencyByCode?.AgencyId > 0)
        {
            agency = agencyByCode;
        }

        return agency;
    }

    public int ToInt(double? value)
    {
        return value.HasValue ? Convert.ToInt32(value.Value) : 0;
    }

    public decimal ToDecimal(double? value, decimal defaultValue = 0m)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return defaultValue;
        }

        double doubleValue = value.Value;
        return doubleValue > (double)decimal.MaxValue || doubleValue < (double)decimal.MinValue ? defaultValue : (decimal)doubleValue;
    }

    public int GetCreditCardTypeId(string? cardType)
    {
        return cardType?.ToLowerInvariant() switch
        {
            "visa" => (int)Diamond.Core.CreditCardType.VISA,
            "mastercard" => (int)Diamond.Core.CreditCardType.MasterCard,
            "discover" => (int)Diamond.Core.CreditCardType.Discover,
            "american express" or "amex" or "americanexpress" or "american_express" => (int)Diamond.Core.CreditCardType.AmericanExpress,
            _ => (int)Diamond.Core.CreditCardType.None,
        };
    }

    public async Task<bool> SaveCCAndSetToDefault(PaymentInfo paymentInfo)
    {
        var saveCCTokenRequest = new SaveCCTokenRequest
        {
            ClientId = paymentInfo.DiamondClientId,
            CustomerProfileId = paymentInfo.CustomerProfileId,
            NewPaymentProfileId = paymentInfo.PaymentProfileId,
            CardNumber = paymentInfo.LastFour,
            ExpirationMonth = paymentInfo.ExpirationMonth,
            ExpirationYear = paymentInfo.ExpirationYear,
            CardholderName = paymentInfo.CardholderName,
            CreditCardTypeId = GetCreditCardTypeId(paymentInfo.CardType),
            PolicyId = paymentInfo.PolicyId
        };

        var tokenResponse = await _diamondService.SaveCCToken(saveCCTokenRequest);

        bool success = tokenResponse?.Success ?? false;

        if (success)
        {
            var saveCCDefaultTokenRequest = new SaveCCTokenRequest
            {
                ClientId = paymentInfo.DiamondClientId,
                CustomerProfileId = paymentInfo.CustomerProfileId,
                DefaultPaymentProfileId = paymentInfo.PaymentProfileId,
                PolicyId = paymentInfo.PolicyId
            };

            var defaultTokenResponse = await _diamondService.SaveCCDefaultToken(saveCCDefaultTokenRequest);

            success = defaultTokenResponse?.Success ?? false;
        }

        return success;
    }

    public async Task<bool> SaveEftAndSetToDefault(PaymentInfo paymentInfo)
    {
        var saveEftTokenRequest = new SaveEftTokenRequest()
        {
            ClientId = paymentInfo.DiamondClientId,
            CustomerProfileId = paymentInfo.CustomerProfileId,
            NewPaymentProfileId = paymentInfo.PaymentProfileId,
            AccountNumber = paymentInfo.LastFour,
            PolicyId = paymentInfo.PolicyId
        };

        var tokenResponse = await _diamondService.SaveEftToken(saveEftTokenRequest);

        bool success = tokenResponse?.Success ?? false;

        if (success)
        {
            var saveEftDefaulTokenRequest = new SaveEftTokenRequest()
            {
                ClientId = paymentInfo.DiamondClientId,
                CustomerProfileId = paymentInfo.CustomerProfileId,
                DefaultPaymentProfileId = paymentInfo.PaymentProfileId,
                PolicyId = paymentInfo.PolicyId
            };

            var defaultTokenResponse = await _diamondService.SaveEftDefaultToken(saveEftDefaulTokenRequest);
            success = defaultTokenResponse?.Success ?? false;
        }

        return success;
    }

    public async Task<bool> SetDefaultCCPayment(int diamondClientId, string paymentProfileId, string customerProfileId, int policyId)
    {
        var saveCCDefaultTokenRequest = new SaveCCTokenRequest
        {
            ClientId = diamondClientId,
            CustomerProfileId = customerProfileId,
            DefaultPaymentProfileId = paymentProfileId,
            PolicyId = policyId
        };

        var setDefaultTokenResponse = await _diamondService.SaveCCDefaultToken(saveCCDefaultTokenRequest);

        return setDefaultTokenResponse?.Success ?? false;
    }

    public async Task<bool> SetDefaultEftPayment(int diamondClientId, string paymentProfileId, string customerProfileId, int policyId)
    {
        var saveEftDefaulTokenRequest = new SaveEftTokenRequest()
        {
            ClientId = diamondClientId,
            CustomerProfileId = customerProfileId,
            DefaultPaymentProfileId = paymentProfileId,
            PolicyId = policyId
        };

        var setDefaultTokenResponse = await _diamondService.SaveEftDefaultToken(saveEftDefaulTokenRequest);

        return setDefaultTokenResponse?.Success ?? false;
    }

    #endregion CompletePurchase Methods

    private async Task<Dictionary<Tuple<int, int, int>, decimal>> CreatePremiumDictionary(List<Diamond.Core.Models.RateOnly.CoverageAdditionalInfoCollection> coverageInfos, QuoteDto quote, decimal additionalPrice = 0)
    {
        var versionId = quote.VersionId;
        var premiums = new Dictionary<Tuple<int, int, int>, decimal>();

        if (coverageInfos == null)
        {
            return premiums;
        }
        //var versionData = await GetCachedVersionData(versionId);
        foreach (var coverageInfo in coverageInfos)
        {
            Decimal.TryParse(coverageInfo.Value, out decimal premium);
            var total = premium;

            var coverageInfoParts = coverageInfo.Description.Split('-').Select(c => c.Trim());

            if (coverageInfoParts.Count() < 3)
            {
                continue;
            }

            var Plan = (await GetCoverageLimits(_versionData))
                .FirstOrDefault(cl => cl.Description.Equals(coverageInfoParts.ElementAt(0), StringComparison.CurrentCultureIgnoreCase));

            var Deductible = _versionData.Deductibles
                .FirstOrDefault(d => d.Description.Equals(coverageInfoParts.ElementAt(1), StringComparison.CurrentCultureIgnoreCase));

            var Reimbursement = _versionData.CoinsuranceTypes
                .FirstOrDefault(ct => ct.Description.Equals(coverageInfoParts.ElementAt(2), StringComparison.CurrentCultureIgnoreCase));

            if (Plan == null || Deductible == null || Reimbursement == null)
            {
                continue;
            }

            var differentPlan = (int)quote.Plan != Plan?.CoverageLimitId;
            var differentDeductible = (int)quote.Deductible != Deductible.DeductibleId;
            var differentReimbursement = (int)quote.Reimbursement != Reimbursement?.CoinsuranceTypeId;
            var tuple = new Tuple<int, int, int>(Plan?.CoverageLimitId ?? 0, Deductible.DeductibleId, Reimbursement?.CoinsuranceTypeId ?? 0);

            if (premium > 0 && (differentPlan || differentDeductible || differentReimbursement))
            {
                total += additionalPrice;
            }

            premiums.Add(tuple, total);
        }

        return premiums;
    }

    private async Task<QuoteRateResponseDto> CreateQuoteRateSelectedRate(
                    QuoteRequestLegacyDto quoteRequest,
                    ZipCode zipCodeInfo,
                    bool isRate = true)
    {
        bool isCC = false;
        var response = await GetInsuranceInformation(quoteRequest.EffectiveDate, zipCodeInfo.StateAbbr, quoteRequest.IsEB, isCC, quoteRequest.Partner.PartnerGuid ?? "");

        if (response.insuranceProduct != null)
        {
            RemoveInsuranceModifiersDiscounts(response.insuranceProduct, quoteRequest);
        }

        response.petQuoteResponseList = new List<PetQuoteRateResponseDto>();

        bool multiplePetDiscount = quoteRequest.petQuotes.Count > 1 || quoteRequest.isMultiplePets;
        foreach (var petQuote in quoteRequest.petQuotes)
        {
            if (!petQuote.IsOpeningQuote && (petQuote.modifiers == null || petQuote.modifiers.Count == 0))
            {
                petQuote.IsInitialRate = true;
            }

            int petTypeId = await ValidatePetBreed(petQuote, response, zipCodeInfo);

            var petQuoteResponse = await BuildQuoteResponseSelectedRate(quoteRequest, petQuote, multiplePetDiscount);

            var ratePetQuoteResponse = MapQuoteData(petQuoteResponse, petQuote, quoteRequest.groupCode, petTypeId, petQuote.userSelectedInfoPlan?.PrePackagedPlanId);

            ratePetQuoteResponse.InsuranceModifiers = GetInsuranceModifiers(petQuote, response.insuranceProduct?.InsuranceModifiers.Clone());
            ratePetQuoteResponse.InsuranceModifiers = DynamicModifiers(ratePetQuoteResponse.InsuranceModifiers, petQuoteResponse.DynamicModifiers);
            ratePetQuoteResponse.InsuranceModifiers = SetIsSelectedDiscounts(petQuoteResponse.Discounts, ratePetQuoteResponse.InsuranceModifiers);
            SetIsSelectedDefaults(ratePetQuoteResponse.InsuranceModifiers, quoteRequest.IsOpenQuote, petQuote);

            response.petQuoteResponseList.Add(ratePetQuoteResponse);
        }

        response.effectiveDate = quoteRequest.effectiveDate;
        response.zipCode = quoteRequest.zipCode;
        response.stateAbrv = zipCodeInfo.StateAbbr;
        response.groupCode = await ShortPromoCode(quoteRequest.groupCode);
        response.groupCodeDscr = quoteRequest.groupCodeDscr;
        response.isMultiplePets = quoteRequest.isMultiplePets;
        response.CoverageInformation = await GetCoverageInformationDetails(response.insuranceProduct?.Id ?? 0);
        response.SiteMessages = await GetMessageSetting(zipCodeInfo.StateAbbr, quoteRequest.EffectiveDate);

        if (response.insuranceProduct != null)
        {
            response.insuranceProduct.InsuranceModifiers.ToList().ForEach(item =>
            {
                item.OptionalBenefitsDetailsItem.ForEach(o =>
                {
                    if (o.BulletIcon != null)
                    {
                        o.BulletIcon = GetUrlImage(o.BulletIcon);
                    }
                });
            });
        }

        if (response.petQuoteResponseList != null)
        {
            response.petQuoteResponseList.ForEach(item =>
            {
                item.InsuranceModifiers?.ForEach(x =>
                {
                    x.OptionalBenefitsDetailsItem.ForEach(y =>
                    {
                        if (y.BulletIcon != null)
                        {
                            y.BulletIcon = GetUrlImage(y.BulletIcon);
                        }
                    });
                });
            });
        }

        return response;
    }

    private static ICollection<InsuranceModifierEB> DynamicModifiers(ICollection<InsuranceModifierEB> insuranceModifiersEB, IList<DynamicModifierDto> dynamicModifierDtos)
    {
        var insuranceModifiersResponse = insuranceModifiersEB.Select(m => (InsuranceModifierEB)m.Clone()).ToList();

        var productDynamicModifiers = insuranceModifiersResponse.Where(m => m.BenefitFeeType == BenefitFeeType.Dynamic);

        if (!dynamicModifierDtos.Any())
        {
            return insuranceModifiersResponse;
        }

        foreach (var item in productDynamicModifiers)
        {
            item.FeeAmount = dynamicModifierDtos.FirstOrDefault(m =>
            m.PmsModifierId == item.PMSModifierId.GetValueOrDefault())?.FeeAmount;
        }

        return insuranceModifiersResponse;
    }

    private async Task<Diamond.Core.Models.Policy.Agency?> GetAgencyData(int agencyId)
    {
        var response = await _diamondService.LoadAgency(agencyId);
        return response != null && response != null ? response.Agency : null;
    }

    private async Task<int> GetAgencyId(PolicyImage pImage, string? groupPartnerCode, SystemData? systemData)
    {
        ReadPropertiesRecursive(pImage);
        var ver = systemData?.Versions.FirstOrDefault(x => x.VersionId == pImage.VersionId);
        int agencyId = -1;

        if (ver == null || String.IsNullOrEmpty(groupPartnerCode))
        {
            return agencyId;
        }

        var response = await _diamondService.LoadPolicyPromoConfig(groupPartnerCode);

        var policyPromoCategoryConfigs = response?.Where(x =>
               (x.CompanyStateLobId == ver.CompanyStateLobId || x.CompanyStateLobId == 0)
               && !String.IsNullOrEmpty(x.AdditionalInfo)
               && !String.IsNullOrWhiteSpace(x.AdditionalInfo));

        if (policyPromoCategoryConfigs == null || !policyPromoCategoryConfigs.Any())
        {
            return agencyId;
        }

        var agencyIdValue = await _diamondService.GetAgencyIdByCode(policyPromoCategoryConfigs?.FirstOrDefault()?.AdditionalInfo);

        if (agencyIdValue != null && agencyIdValue > 0)
        {
            agencyId = agencyIdValue;
        }

        return agencyId;
    }

    private static List<TaxDetailDto> GetAnnualTax(List<Diamond.Core.Models.RateOnly.Coverage> coverages, string state)
    {
        var taxesList = new List<TaxDetailDto>();

        if (coverages == null || coverages.Count == 0)
        {
            return taxesList;
        }

        switch (state)
        {
            case "KY":

                var stateTax = (decimal)(coverages.Where(c => c.CoverageCodeID == (int)CoverageCode.PolicyLevelStateTaxKY).FirstOrDefault()?.FullTermPremium ?? 0);
                var countryTax = (decimal)(coverages.Where(c => c.CoverageCodeID == (int)CoverageCode.PolicyLevelCountyTaxKY).FirstOrDefault()?.FullTermPremium ?? 0);
                var municipalTax = (decimal)(coverages.Where(c => c.CoverageCodeID == (int)CoverageCode.PolicyLevelMunicipalTaxKY).FirstOrDefault()?.FullTermPremium ?? 0);
                var collectionFee = (decimal)(coverages.Where(c => c.CoverageCodeID == (int)CoverageCode.TaxCollectionFeeKY).FirstOrDefault()?.FullTermPremium ?? 0);

                var taxes = new TaxDetailDto()
                {
                    Amount = Math.Round(stateTax + countryTax + municipalTax, 2, MidpointRounding.AwayFromZero),
                    Description = TAXES
                };

                taxesList.Add(taxes);

                if (collectionFee != 0)
                {
                    var taxCollectionFee = new TaxDetailDto()
                    {
                        Amount = Math.Round(collectionFee, 2, MidpointRounding.AwayFromZero),
                        Description = TAX_COLLECTION_FEE
                    };

                    taxesList.Add(taxCollectionFee);
                }
                break;

            default:
                var coverage = coverages.Where(c => c.CoverageCodeID == (int)CoverageCode.VATax).FirstOrDefault();

                if (coverage != null)
                {
                    var tax = new TaxDetailDto
                    {
                        Amount = Math.Round((decimal)(coverage.FullTermPremium ?? 0), 2, MidpointRounding.AwayFromZero),
                        Description = TAXES
                    };

                    taxesList.Add(tax);
                }
                break;
        }

        return taxesList;
    }

    private async Task<List<InsuranceModifierByState>> GetByInsuranceModifiers(bool isEB, InsuranceProduct insuranceProduct)
    {
        var stateFactorId = insuranceProduct?.SelectedStateFactor?.Id ?? 0;
        var cacheKey = $"Figo.Static.Rate.InsuranceModifierByStates";
        if (isEB)
        {
            if (_insuranceModifierEBByStatesList == null)
            {
                cacheKey = $"Figo.Static.Rate.InsuranceModifierEBByStates";
                _insuranceModifierEBByStatesList = await _cache.GetOrCreateAsync(cacheKey, async () => await _context.InsuranceModifierEBByStates.AsNoTracking().ToListAsync().ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);
            }

            var insuranceModifierEBByStates = _insuranceModifierEBByStatesList.Clone();

            var entitiesEB = insuranceModifierEBByStates.Where(x => x.IsActive
                && x.InsuranceStateFactorEBId == stateFactorId).ToList();

            return _mapper.Map<List<InsuranceModifierEBByState>, List<InsuranceModifierByState>>(entitiesEB);
        }

        if (_insuranceModifierByStates == null)
        {
            _insuranceModifierByStates = await _cache.GetOrCreateAsync(cacheKey, async () => await _context.InsuranceModifierByStates.AsNoTracking().ToListAsync().ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);
        }

        var insuranceModifierByStates = _insuranceModifierByStates.Clone();

        return insuranceModifierByStates.Where(x => x.IsActive && x.InsuranceStateFactorId == stateFactorId).ToList();
    }

    private async Task<ZipCode?> GetByZipcode(string zipCode)
    {
        try
        {
            if (_zipCode == null)
            {
                string zipCodeNumber = zipCode.Substring(0, 5);
                var cacheKey = $"Figo.Static.Rate.ZipCode.{zipCodeNumber}";
                _zipCode = await _cache.GetOrCreateAsync(cacheKey, async () => await _context.ZipCodes.AsNoTracking().FirstOrDefaultAsync(z => z.ZIPCode == zipCodeNumber).ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);
            }

            return _zipCode;
        }
        catch
        {
            return null;
        }
    }

    private async Task<ZipCode> GetByZipcodeThrowWhenNULL(string zipCode)
    {
        var zipcodeInfo = await GetByZipcode(zipCode);

        if (zipcodeInfo == null)
        {
            throw new FigoException($"Zip code {zipCode} does not exists.");
        }

        return zipcodeInfo;
    }

    private Task<SystemData> GetCachedSystemData()
    {
        var key = $"Diamond.SystemData";
        return _cache.GetOrCreateAsync(key, async () => await _diamondService.GetSystemData(), TimeSpan.FromDays(5), CancellationToken.None);
    }

    private Task<VersionData> GetCachedVersionData(int versionId)
    {
        var key = $"Diamond.VersionData.{versionId}";
        return _cache.GetOrCreateAsync(key, async () => await _diamondService.GetVersionData(versionId), TimeSpan.FromDays(5), CancellationToken.None);
    }

    private async Task<InsuranceProductEB?> GetCloneDataEBIfNecessary(InsuranceProductEB productEB, int productEBId)
    {
        if (productEB?.IsCompleteInsuranceProduct() == true)
        {
            return productEB;
        }

        var data = await GetInsuranceProductEBData(productEBId);

        return data != null ? data.Clone() : null;
    }

    private async Task<InsuranceProduct?> GetCloneDataIfNecessary(InsuranceProduct product, int productId)
    {
        if (product?.IsCompleteInsuranceProduct() == true)
        {
            return product;
        }

        var data = await GetInsuranceProductData(productId);

        return data != null ? data.Clone() : null;
    }

    private async Task<List<CoverageInformation?>?> GetCoverageInformationByProduct(int InsuranceProductId)
    {
        var key = $"Figo.Static.CoverageInformation.{InsuranceProductId}";

        var coverageInformation = (await _cache.GetAsync<List<CoverageInformation?>>(key)).Value;

        if (coverageInformation == null)
        {
            coverageInformation = await _context.CoverageInformationsByProduct.Include(x => x.CoverageInformation).AsNoTracking().Where(x => x.InsuranceProductId == InsuranceProductId).Select(y => y.CoverageInformation).ToListAsync();
            await _cache.SetAsync(key, coverageInformation, TimeSpan.FromDays(365), default);
        }

        return coverageInformation;
    }

    private async Task<CoverageInformationDto> GetCoverageInformationDetails(int insuranceProductId)
    {
        var result = new CoverageInformationDto
        {
            WhatsCovered = new List<WhatsCoveredDto>(),
            NotCovered = new List<NotCoveredDto>()
        };

        var coverageInformation = await GetCoverageInformationByProduct(insuranceProductId);

        coverageInformation?.Where(x => x != null && x.IsCoveraged).ForEach(y =>
        {
            if (y != null)
            {
                result.WhatsCovered.Add(new WhatsCoveredDto
                {
                    Id = y.Id,
                    Icon = GetUrlImage(y.Icon),
                    Title = y.Title,
                    Text = y.Text,
                    Order = y.Order
                });
            }
        });

        coverageInformation?.Where(x => x != null && !x.IsCoveraged).ForEach(y =>
        {
            if (y != null)
            {
                result.NotCovered.Add(new NotCoveredDto
                {
                    Id = y.Id,
                    Icon = GetUrlImage(y.Icon),
                    Title = y.Title,
                    Order = y.Order
                });
            }
        });

        return result;
    }

    private static async Task<List<CoverageLimitVersion>> GetCoverageLimits(VersionData versionData)
    {
        List<CoverageLimitVersion> coverageLimits = new List<CoverageLimitVersion>();

        foreach (var coverageLimit in versionData.CoverageLimits)
        {
            if (coverageLimit.CoverageCodeId != 1)
            {
                continue;
            }

            coverageLimits.Add(coverageLimit);
        }
        return coverageLimits;
    }

    private async Task<List<PlanDto>> GetCoveragesLimit(int versionId)
    {
        List<PlanDto> plans = new List<PlanDto>();
        List<CoverageLimitVersion> coveragesLimit = await GetCoverageLimits(_versionData);
        foreach (var coverage in coveragesLimit)
        {
            plans.Add(new PlanDto
            {
                Id = coverage.CoverageLimitId,
                Description = coverage.Description
            });
        }
        return plans;
    }

    private async Task<int?> GetCustomerIdByDiamondClientId(int diamondClientId)
    {
        string diamondClientIdValue = diamondClientId.ToString();
        var customerMarket = await _context.CustomerMarketChannels.AsNoTracking().FirstOrDefaultAsync(x => x.SourceCustomerId == diamondClientIdValue
            && x.MarketChannelId == (int)MarketingChannel.Figo).ConfigureAwait(false);

        if (customerMarket is null)
        {
            return null;
        }

        return customerMarket.PetCloudCustomerId;
    }

    private async Task<List<QuoteDeductibleDto>> GetDeductibles(int versionId = 1)
    {
        var returnValue = new List<QuoteDeductibleDto>();
        //var versionData = await GetCachedVersionData(versionId);
        foreach (var insDeductible in _versionData.Deductibles)
        {
            var deductible = new QuoteDeductibleDto
            {
                Id = insDeductible.DeductibleId,
                DollarVal = insDeductible.Deductible,
                Description = insDeductible.Deductible.ToString("C0")
            };

            returnValue.Add(deductible);
        }

        returnValue.Sort((d1, d2) => d1.DollarVal.CompareTo(d2.DollarVal) * -1);

        return returnValue;
    }

    private async Task<int> GetDefaultAgencyId(int companyId, int stateId, int lobId, PolicyImage pImage, string? groupPartnerCode, SystemData? systemData)
    {
        int agencyId = await GetAgencyId(pImage, groupPartnerCode, systemData);

        if (agencyId > -1)
        {
            return agencyId;
        }

        var stopwatch = new System.Diagnostics.Stopwatch();
        agencyId = DEFAULT_AGENCY_ID;
        stopwatch.Start();

        var agencies = await _diamondService.GetAgencyData(stateId, companyId, lobId);

        if (agencies == null || agencies.Count == 0)
        {
            return agencyId;
        }

        var agy = agencies.FirstOrDefault(x => x.Code == DEFAULT_AGENCY_CODE);

        if (agy == null)
        {
            return agencyId;
        }

        agencyId = agy.AgencyId;

        stopwatch.Stop();
        System.Diagnostics.Trace.WriteLine(String.Format("Insuresoft Get Agencies: {0} ms", stopwatch.ElapsedMilliseconds));

        return agencyId;
    }

    private async Task<List<DiscountDto>> GetDiscounts(List<Diamond.Core.Models.RateOnly.Modifier> ratedModifiers, ICollection<InsuranceModifierEB> insuranceModifierEBs)
    {
        var discountModifiers = insuranceModifierEBs.Where(
            m => m.InsuranceModifierTypeEBId == (int)ModifierTypeEnum.DISCOUNT);

        if (_insuranceModifierDiscountsList == null)
        {
            var cacheKey = $"Figo.Static.Rate.InsuranceModifierDiscounts";
            _insuranceModifierDiscountsList = await _cache.GetOrCreateAsync(cacheKey, async () => await _context.InsuranceModifierDiscounts.AsNoTracking().ToListAsync().ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);
        }

        var discountInformation = _insuranceModifierDiscountsList.Clone();

        var discounts = from modifier in ratedModifiers
                        join discount in discountModifiers
                            on modifier.ModifierTypeId equals discount.PMSModifierId
                        join info in discountInformation
                            on discount.PMSModifierId equals info.PMSModifierId
                        where discount.IsVisible == true && modifier.CheckboxSelected == true
                        select new DiscountDto
                        {
                            Id = discount.PMSModifierId == null ? 0 : (int)discount.PMSModifierId,
                            Description = discount.InputText,
                            InsuranceModifierDiscount = info
                        };

        return discounts.ToList();
    }

    private List<DynamicModifierDto> GetDynamicModifiers(PolicyImageRate policyRated, ICollection<InsuranceModifierEB> insuranceModifiers)
    {
        var dynamicModifiers = insuranceModifiers.Where(m => m.BenefitFeeType == BenefitFeeType.Dynamic);

        if (!dynamicModifiers.Any())
        {
            return new List<DynamicModifierDto>();
        }

        var coverages =
            from coverage in policyRated.LOB.PolicyLevel.Coverages
            join modifier in dynamicModifiers
                on coverage.CoverageCodeID equals modifier.PMSModifierId.GetValueOrDefault()
            select new DynamicModifierDto
            {
                PmsModifierId = coverage.CoverageCodeID != null ? (int)coverage.CoverageCodeID : 0,
                FeeAmount = Math.Round(((double)(coverage.FullTermPremium ?? 0) / MONTHS), 2, MidpointRounding.AwayFromZero)
            };

        return coverages.ToList();
    }

    private async Task<QuoteRateResponseDto> GetInsuranceInformation(DateTime effDate, string stateAbbr, bool isEb, bool isCC = false, string? partnerGuid = null)
    {
        QuoteRateResponseDto response = new QuoteRateResponseDto
        {
            ebPetQuoteResponseList = new List<PetQuoteRateResponseDto>()
        };

        var insuranceProductData = await GetInsuranceProductByIsEBAndStateFactor(new InsuranceProductRequestDto
        {
            BuildBundles = true,
            EffectiveDate = effDate,
            IsEB = isEb,
            StateAbbr = stateAbbr,
            RemoveModifiers = !isCC,
            PartnerGuid = partnerGuid ?? ""
        });

        response.insuranceProduct = _mapper.Map<InsuranceProductDto>(insuranceProductData);

        return response;
    }

    public async Task<InsuranceProduct?> GetInsuranceProductByIsEBAndStateFactor(InsuranceProductRequestDto request)
    {
        InsuranceProduct? insuranceProduct = null;
        if (request.IsEB)
        {
            var insuranceProductEB = await GetInsuranceProductByStateFactorEB(request.EffectiveDate, request.StateAbbr);
            insuranceProduct = _mapper.Map<InsuranceProductEB, InsuranceProduct>(insuranceProductEB ?? new InsuranceProductEB { Name = "" });
        }
        else
        {
            insuranceProduct = await GetInsuranceProductByStateFactor(request.EffectiveDate, request.StateAbbr);
        }

        if (request.RemoveModifiers && insuranceProduct != null)
        {
            await RemoveInsuranceModifiers(request.IsEB, insuranceProduct, request.PartnerGuid);
        }

        if (request.BuildBundles && insuranceProduct != null)
        {
            insuranceProduct.BuildBundles();
        }

        return insuranceProduct;
    }

    private async Task<InsuranceProduct?> GetInsuranceProductByStateFactor(DateTime effectiveDate, string stateAbbr)
    {
        var stateFactor = await GetProductByStateFactor(effectiveDate, stateAbbr);

        if (stateFactor == null)
        {
            throw new FigoException("No product available for this quote.");
        }

        var insuranceProduct = await GetOrSetCacheInsuranceProduct(stateFactor.InsuranceProductId);

        if (insuranceProduct != null)
        {
            insuranceProduct.InsuranceWaitingPeriods = await InsuranceWaitingPeriodByStates(stateFactor.StateId);

            insuranceProduct.SelectedStateFactor = stateFactor;
        }

        return insuranceProduct;
    }

    private async Task<List<InsuranceWaitingPeriodByState>> InsuranceWaitingPeriodByStates(int stateId)
    {
        if (_insuranceWaitingPeriodByStatesList == null)
        {
            var cacheKey = $"Figo.Static.Rate.InsuranceWaitingPeriodByStates";
            _insuranceWaitingPeriodByStatesList = await _cache.GetOrCreateAsync(cacheKey, async () => await _context.InsuranceWaitingPeriodByStates.AsNoTracking().Include(x => x.InsuranceWaitingPeriodType).ToListAsync().ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);
        }
        return _insuranceWaitingPeriodByStatesList.Clone().Where(x => x.StateId == stateId).ToList();
    }

    private async Task<InsuranceProductEB?> GetInsuranceProductByStateFactorEB(DateTime effectiveDate, string stateAbbr)
    {
        var stateFactor = await GetProductByStateFactorEB(effectiveDate, stateAbbr);

        if (stateFactor == null)
        {
            throw new FigoException("No product available for this quote.");
        }

        var insuranceProductEB = await GetOrSetCacheInsuranceProductEB(stateFactor.InsuranceProductEBId);

        if (insuranceProductEB != null)
        {
            insuranceProductEB.InsuranceWaitingPeriodsEB = await GetInsuranceWaitingPeriodByStatesEB(stateFactor.StateId);

            insuranceProductEB.SelectedStateFactorEB = stateFactor;
        }

        return insuranceProductEB;
    }

    private async Task<List<InsuranceWaitingPeriodByStateEB>> GetInsuranceWaitingPeriodByStatesEB(int stateId)
    {
        if (_insuranceWaitingPeriodByStatesEBList == null)
        {
            var cacheKey = $"Figo.Static.Rate.InsuranceWaitingPeriodByStatesEB";
            _insuranceWaitingPeriodByStatesEBList = await _cache.GetOrCreateAsync(cacheKey, async () => await _context.InsuranceWaitingPeriodByStatesEB.AsNoTracking().Include(x => x.InsuranceWaitingPeriodTypeEB).ToListAsync().ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);
        }

        return _insuranceWaitingPeriodByStatesEBList.Clone().Where(x => x.StateId == stateId).ToList();
    }

    private async Task<InsuranceProduct?> GetInsuranceProductData(int productId)
    {
        var insuranceProductData = await _context.InsuranceProducts.AsNoTracking().Include(x => x.InsurancePolicyConfigurations)
            .ThenInclude(x => x.InsurancePolicyDefaultCoverages).ThenInclude(x => x.InsurancePolicyCoverageType).FirstOrDefaultAsync(x => x.Id == productId).ConfigureAwait(false);

        if (insuranceProductData != null)
        {
            insuranceProductData.InsuranceInformationSections = await _context.InsuranceInformationSections.AsNoTracking()
                .Include(x => x.InsuranceInformationDetails).Where(x => x.InsuranceProductId == insuranceProductData.Id).ToListAsync().ConfigureAwait(false);

            insuranceProductData.InsuranceMultiModalItems = await _context.InsuranceMultiModalItems.AsNoTracking().Where(x => x.InsuranceProductId == insuranceProductData.Id).ToListAsync().ConfigureAwait(false);

            insuranceProductData.InsuranceProductCoverages = await _context.InsuranceProductCoverages.AsNoTracking()
                .Include(x => x.InsuranceProductCoverageXInsuranceProductPlans).ThenInclude(x => x.InsuranceProductPlan).Where(x => x.InsuranceProductId == insuranceProductData.Id).ToListAsync().ConfigureAwait(false);

            insuranceProductData.InsuranceProductFee = await _context.InsuranceProductFees.AsNoTracking().Include(x => x.InsuranceFee).Where(x => x.InsuranceProductId == insuranceProductData.Id).ToListAsync().ConfigureAwait(false);

            insuranceProductData.InsuranceModifiers = await _context.InsuranceModifiers.AsNoTracking().Include(x => x.OptionalBenefitsDetailsItem).Where(x => x.InsuranceProductId == insuranceProductData.Id).ToListAsync().ConfigureAwait(false);
        }

        return insuranceProductData;
    }

    private async Task<PrePackagedPlanValidOptionsByStateDto?> GetInsuranceProductDedReimbValidOptions(int stateId, int insuranceProductId, int petAge, string? employerGuid = null, string? partnerGuid = null)
    {
        var packagesByAge = await GetPrePackagedPlanValidOptionsByAge(petAge).ConfigureAwait(false);

        var filteredPackages = new List<PrePackagedPlanValidOptionsByState>();
        bool isPrepackagedPlanByEmployeer = false;
        PrePackagedPlanValidOptionsByStateDto? result = null;

        if (!string.IsNullOrEmpty(employerGuid))
        {
            filteredPackages = packagesByAge.Where(x => x.Employer != null && x.Employer.GuID == employerGuid).ToList();
        }

        if (filteredPackages.Count == 0)
        {
            filteredPackages = packagesByAge.Where(x => x.EmployerId == null && ((x.StateId == stateId && x.InsuranceProductId == insuranceProductId && x.Age == petAge) ||
                                   (x.InsuranceProductId == null && x.StateId == null && x.Age == petAge))).ToList();
        }
        else
        {
            isPrepackagedPlanByEmployeer = true;
        }

        var validOptions = _mapper.Map<List<PrePackagedPlanValidOptionsByState>, List<PrePackagedPlanValidOptionsByStateDto>>(filteredPackages);

        if (validOptions.Count == 1)
        {
            result = validOptions.FirstOrDefault();
        }
        else
        {
            if (isPrepackagedPlanByEmployeer)
            {
                result = validOptions.Where(x => x.InsuranceProductId != null && x.StateId != null && x.EmployerId != null).FirstOrDefault();
            }
            else
            {
                result = validOptions.Where(x => x.InsuranceProductId != null && x.StateId != null && x.EmployerId == null).FirstOrDefault();
            }
        }

        if (await _featureManager.IsEnabledAsync(Feature.NewAnnualCoverageOptions))
        {
            int originId = await GetOriginId(partnerGuid, employerGuid != null);
            var pmsCoverageLimits = await GetCoverageLimitExceptionsByState(stateId, originId).ConfigureAwait(false);
            var pmsCoverageLimitExceptions = pmsCoverageLimits.Where(x => x.StateId != null).ToList();

            if (result != null)
            {
                if (!pmsCoverageLimitExceptions.Any())
                {
                    result.PMSCoverageLimits = pmsCoverageLimits.Where(x => x.StateId == null && x.OriginId == null).Select(x => x.PMSCoverageLimitId).ToList();
                }
                else
                {
                    result.PMSCoverageLimits = pmsCoverageLimitExceptions.Select(x => x.PMSCoverageLimitId).ToList();
                }
            }
        }

        return result;
    }

    private async Task<List<CoverageLimitExceptionsByState>> GetCoverageLimitExceptionsByState(int stateId, int? originId = null)
    {
        if (_pmsCoverageLimits == null)
        {
            var cacheKey = $"Figo.Static.Rate.CoverageLimitExceptionsByState";
            _pmsCoverageLimits = await _cache.GetOrCreateAsync(cacheKey, async () => await _context.CoverageLimitExceptionsByState.AsNoTracking().ToListAsync().ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);
        }

        return _pmsCoverageLimits.Where(x => (x.StateId == stateId && x.OriginId == originId) || (x.StateId == null && x.OriginId == null)).ToList();
    }

    private async Task<List<PrePackagedPlanValidOptionsByState>> GetPrePackagedPlanValidOptionsByAge(int petAge)
    {
        List<PrePackagedPlanValidOptionsByState> prePackagedPlanValidOptionsByStates;

        if (_prePackagedPlanValidOptionsByStatesList == null)
        {
            var cacheKey = $"Figo.Static.Rate.PrePackagedPlanValidOptionsByStates";
            _prePackagedPlanValidOptionsByStatesList = await _cache.GetOrCreateAsync(cacheKey, async () => await _context.PrePackagedPlanValidOptionsByStates.AsNoTracking().ToListAsync().ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);
        }

        prePackagedPlanValidOptionsByStates = _prePackagedPlanValidOptionsByStatesList.Where(x => x.Age == petAge).ToList();

        var employersOptions = prePackagedPlanValidOptionsByStates.Where(x => x.EmployerId != null).ToList();

        if (employersOptions != null && employersOptions.Count != 0)
        {
            var employersIds = employersOptions.Select(x => x.EmployerId).ToList();
            var employers = await GetEmployersByIds(employersIds).ConfigureAwait(false);
            prePackagedPlanValidOptionsByStates.ForEach(x =>
            {
                x.Employer = employers.FirstOrDefault(y => y.Id == x.EmployerId);
            });
        }

        return prePackagedPlanValidOptionsByStates;
    }

    private async Task<List<EmployerEB>> GetEmployersByIds(List<int?> employersIds)
    {
        if (_employersEBList == null)
        {
            var ids = string.Join(".", employersIds.Select(x => x.ToString()).ToArray());
            var cacheKey = $"Figo.Static.Rate.EmployersEB.{ids}";
            _employersEBList = await _cache.GetOrCreateAsync(cacheKey, async () => await _context.EmployersEB.AsNoTracking().Where(x => employersIds.Contains(x.Id)).ToListAsync().ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);
        }

        return _employersEBList;
    }

    private async Task<InsuranceProductEB> GetInsuranceProductEB(ZipCode zipCodeInfo, DateTime effDate)
    {
        InsuranceProduct? insuranceProduct = await GetInsuranceProductByIsEBAndStateFactor(new InsuranceProductRequestDto
        {
            BuildBundles = true,
            EffectiveDate = effDate,
            IsEB = true,
            StateAbbr = zipCodeInfo.StateAbbr,
            RemoveModifiers = true
        });

        return _mapper.Map<InsuranceProduct, InsuranceProductEB>(insuranceProduct ?? new InsuranceProduct());
    }

    private async Task<InsuranceProductEB?> GetInsuranceProductEBData(int productEBId)
    {
        var insuranceProductEBData = await _context.insuranceProductsEB.Include(x => x.InsurancePolicyConfigurationsEB)
            .ThenInclude(x => x.InsurancePolicyDefaultCoveragesEB).ThenInclude(x => x.InsurancePolicyCoverageTypeEB).FirstOrDefaultAsync(x => x.Id == productEBId).ConfigureAwait(false);

        if (insuranceProductEBData != null)
        {
            insuranceProductEBData.InsuranceInformationSectionsEB = await _context.InsuranceInformationSectionsEB
                .Include(x => x.InsuranceInformationDetailsEB).Where(x => x.InsuranceProductEBId == insuranceProductEBData.Id).ToListAsync().ConfigureAwait(false);

            insuranceProductEBData.InsuranceMultiModalItemsEB = await _context.InsuranceMultiModalItemsEB.Where(x => x.InsuranceProductEBId == insuranceProductEBData.Id).ToListAsync().ConfigureAwait(false);

            insuranceProductEBData.InsuranceProductCoveragesEB = await _context.InsuranceProductCoveragesEB
                .Include(x => x.InsuranceProductCoverageEBXInsuranceProductPlansEB).ThenInclude(x => x.InsuranceProductPlanEB).Where(x => x.InsuranceProductEBId == insuranceProductEBData.Id).ToListAsync().ConfigureAwait(false);

            insuranceProductEBData.InsuranceProductFeeEB = await _context.InsuranceProductFeesEB.Include(x => x.InsuranceFeeEB).Where(x => x.InsuranceProductEBId == insuranceProductEBData.Id).ToListAsync().ConfigureAwait(false);

            insuranceProductEBData.InsuranceModifiersEB = await _context.InsuranceModifiersEB.Include(x => x.OptionalBenefitsDetailsItem).Where(x => x.InsuranceProductEBId == insuranceProductEBData.Id).ToListAsync().ConfigureAwait(false);
        }

        return insuranceProductEBData;
    }

    private async Task<bool> GetIsMultiplePetsByCustomerId(int customerId)
    {
        foreach (var policyNumber in await _context.Pets.Where(x => x.CustomerId == customerId && !x.Deleted).Include("PetPolicies").Select(z => z.Policy).ToListAsync())
        {
            if (policyNumber == null || string.IsNullOrEmpty(policyNumber.PolicyNumber))
            {
                continue;
            }

            var policy = await GetPolicyActiveImage(policyNumber.PolicyNumber);

            if (policy != null)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<List<SiteMessageSettingsDto>> GetMessageSetting(string stateAbbr, DateTime effectiveDate)
    {
        var cacheKey = $"Figo.Static.Rate.SiteMessageSettings.{stateAbbr}";
        var siteMessageSettings = await _cache.GetOrCreateAsync(cacheKey, async () => await _context.SiteMessageSettings.AsNoTracking().Where(x => x.State != null && x.State.Abbreviation == stateAbbr).ToListAsync().ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);
        var messageSetting = siteMessageSettings.Where(x => x.StartDate <= effectiveDate && x.Active).ToList();
        messageSetting ??= new List<SiteMessageSettings>();

        return _mapper.Map<List<SiteMessageSettings>, List<SiteMessageSettingsDto>>(messageSetting);
    }

    private static List<TaxDetailDto> GetMonthlyTax(List<TaxDetailDto> annualTaxes, string state)
    {
        List<TaxDetailDto> monthly;

        switch (state)
        {
            case "VA":
                monthly = annualTaxes;
                break;

            default:
                monthly = annualTaxes.Select(t => new TaxDetailDto()
                {
                    Amount = Math.Round(t.Amount / 12, 2, MidpointRounding.AwayFromZero),
                    Description = t.Description
                }).ToList();
                break;
        }
        return monthly;
    }

    private async Task<Diamond.Core.Models.Policy.Modifier?> GetOrAddModifier(int modifierTypeId, List<Diamond.Core.Models.Policy.Modifier> modifiers, int versionId)
    {
        Diamond.Core.Models.Policy.Modifier? retModifier = null;

        ModifierType? modifierType = _versionData.ModifierTypes.FirstOrDefault(x => x.ModifierTypeId == modifierTypeId);
        if (modifierType != null)
        {
            retModifier = retModifier = modifiers.FirstOrDefault(x => x.ModifierTypeId == modifierTypeId);
            if (retModifier == null)
            {
                retModifier = new Diamond.Core.Models.Policy.Modifier();
                modifiers.Add(retModifier);
            }
            retModifier.DetailStatusCode = (int)StatusCode.Active;
            retModifier.ModifierTypeId = modifierTypeId;
            retModifier.ParentModifierTypeId = modifierType.ParentModifierTypeId;
            retModifier.ModifierLevelId = modifierType.ModifierLevelId;
            retModifier.ModifierGroupId = modifierType.ModifierGroupId;
        }
        return retModifier;
    }

    private async Task<int> GetOriginId(string? partnerGuid, bool isEB)
    {
        if (isEB)
        {
            return (int)Domain.Enums.Origin.EB;
        }

        if (string.IsNullOrEmpty(partnerGuid))
        {
            return (int)Domain.Enums.Origin.D2C;
        }
        else
        {
            var partnerConfig = await GetPartnerConfig(partnerGuid);

            switch (partnerConfig?.Name)
            {
                case Partner.Costco:
                    return (int)Domain.Enums.Origin.Cotsto;

                default:
                    return (int)Domain.Enums.Origin.GoodDog;
            }
        }
    }

    private async Task<InsuranceProduct?> GetOrSetCacheInsuranceProduct(int productId)
    {
        var key = $"Figo.Static.InsuranceProduct.{productId}";
        var product = (await _cache.GetAsync<InsuranceProduct>(key)).Value;

        product = await GetOrSetInsuranceProduct(product, productId, key);

        product = await GetCloneDataIfNecessary(product, productId);

        return product;
    }

    private async Task<InsuranceProductEB?> GetOrSetCacheInsuranceProductEB(int productEBId)
    {
        var key = $"Figo.Static.InsuranceProductEB.{productEBId}";

        var productEB = (await _cache.GetAsync<InsuranceProductEB>(key)).Value;

        productEB = await GetOrSetInsuranceProductEB(productEB, productEBId, key);

        productEB = await GetCloneDataEBIfNecessary(productEB, productEBId);

        return productEB;
    }

    private async Task<InsuranceProduct> GetOrSetInsuranceProduct(InsuranceProduct? product, int productId, string key)
    {
        if (product?.IsCompleteInsuranceProduct() == true)
        {
            return product;
        }

        var insuranceProductData = await GetInsuranceProductData(productId);

        await _cache.SetAsync(key, insuranceProductData, TimeSpan.FromDays(365), default);

        return (await _cache.GetAsync<InsuranceProduct>(key)).Value;
    }

    private async Task<InsuranceProductEB> GetOrSetInsuranceProductEB(InsuranceProductEB? product, int productEBId, string key)
    {
        if (product?.IsCompleteInsuranceProduct() == true)
        {
            return product;
        }

        var insuranceProductEBData = await GetInsuranceProductEBData(productEBId);

        await _cache.SetAsync(key, insuranceProductEBData, TimeSpan.FromDays(365), default);

        return (await _cache.GetAsync<InsuranceProductEB>(key)).Value;
    }

    public async Task<PartnerConfigData?> GetPartnerConfig(string partnerGuid)
    {
        if (string.IsNullOrEmpty(partnerGuid))
        {
            return null;
        }

        var cacheKey = $"Figo.Static.Rate.PartnerConfigsWithPC.{partnerGuid}";
        var partnerConfigPC = (await _cache.GetAsync<PartnerConfigData?>(cacheKey)).Value;

        if (partnerConfigPC == null)
        {
            var query = await _context.PartnerConfigs.AsNoTracking().Include(x => x.PartnerPromoCodes).FirstOrDefaultAsync(x => x.IsActive && x.PartnerGuid == partnerGuid);
            partnerConfigPC = _mapper.Map<PartnerConfigData>(query);
            await _cache.SetAsync(cacheKey, partnerConfigPC, TimeSpan.FromDays(365), default);
        }

        return partnerConfigPC;
    }

    private async Task<List<QuotePlanDtoEB>> GetPlansInformationEB(List<Diamond.Core.Models.RateOnly.Coverage> coverages, QuoteDto quote, List<QuoteDeductibleDto> deductibles, List<QuoteReimbursementDto> reimbursements, List<InsuranceProductPlanEB> ProductPlans)
    {
        var plans = new List<QuotePlanDtoEB>();
        var coverageInfo = new List<Diamond.Core.Models.RateOnly.CoverageAdditionalInfoCollection>();

        var vetCoverage = coverages.FirstOrDefault(c => c.CoverageCodeID == (int)CoverageCode.VeterinaryFees);

        if (vetCoverage != null)
        {
            coverageInfo = vetCoverage.CoverageAdditionalInfoCollection;
        }

        decimal wellnessIHCPrice = GetWellnessIhcPrice(coverages);

        Dictionary<Tuple<int, int, int>, decimal> premiums = await CreatePremiumDictionary(coverageInfo, quote, wellnessIHCPrice);

        if (premiums != null && ProductPlans.Count > 0)
        {
            foreach (var plan in ProductPlans)
            {
                plans.Add(CreatePlanEB(premiums, plan, deductibles, reimbursements));
            }
        }
        return plans;
    }

    private async Task<PolicyImage?> GetPolicyActiveImage(string policyNumber)
    {
        PolicyImage? ret = null;
        var lookup = await _diamondService.GetPolicyIdAndNumForPolicyNumber(policyNumber);
        if (lookup != null)
        {
            ret = await GetPolicyActiveImageNumber(lookup.PolicyId);
        }
        return ret;
    }

    private async Task<PolicyImage?> GetPolicyActiveImageNumber(int policyId)
    {
        PolicyImage? ret = null;
        var policyHistories = await _diamondService.GetPolicyHistory(new Diamond.Services.GetPolicies.GetPolicyHistoryQuery(policyId));
        var policyOrderDate = policyHistories.OrderByDescending(x => x.TransactionDate);
        Diamond.Services.GetPolicies.DiamondPolicyHistoryDto? history = new Diamond.Services.GetPolicies.DiamondPolicyHistoryDto();

        history = policyOrderDate.OrderByDescending(x => x.PolicyImageNum).FirstOrDefault(x =>
        (x.PolicyStatusCodeId == (int)PolicyStatusCode.Active || x.PolicyStatusCodeId == (int)PolicyStatusCode.Future));

        if (history != null)
        {
            ret = await _diamondService.LoadImage(history.PolicyId, history.PolicyImageNum);
        }
        return ret;
    }

    private Task<QuoteResponseDto> GetPolicyRate(QuoteDto quote, InsuranceProductEB product, bool multiplePetDiscount)
    {
        return GetPolicyRateOnlyEB(quote, product, multiplePetDiscount);
    }

    private async Task<QuoteResponseDto> GetPolicyRateOnlyEB(QuoteDto quote, InsuranceProductEB product, bool multiplePetDiscount)
    {
        PolicyImage policyImage = await CreatePolicyDiamondEB(quote, product, multiplePetDiscount);
        PolicyImageRate policyRated = await GetRateOnlyByPolicyImage(policyImage);
        return await MapQuoteDataEB(policyRated, quote, product);
    }

    private async Task<string> GetPrepackagedPlanDisclaimerByOrigin(int stateId, int insuranceProductId, int petAge, bool isEB, string partnerGuid, string? employerGuid)
    {
        int originId = await GetOriginId(partnerGuid, isEB);
        string disclaimer = string.Empty;

        var cacheKey = $"Figo.Static.Rate.PrePackagedPlanDisclaimersByOrigin";
        var prePackagedPlanDisclaimersByOrigin = await _cache.GetOrCreateAsync(cacheKey, async () => await _context.PrePackagedPlanDisclaimersByOrigin.AsNoTracking().Include(x => x.EmployerEB).ToListAsync().ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);

        if (!string.IsNullOrEmpty(employerGuid))
        {
            var disclaimerConfigurationEB = prePackagedPlanDisclaimersByOrigin.Where(x => ((x.EmployerEB != null && x.EmployerEB.GuID == employerGuid) && x.Age == petAge && x.OriginId == (int)Domain.Enums.Origin.EB) || x.OriginId == (int)Domain.Enums.Origin.EB && x.Age == petAge && x.EmployerId == null).ToList();
            if (disclaimerConfigurationEB.Count == 1)
            {
                disclaimer = disclaimerConfigurationEB.FirstOrDefault()?.Disclaimer ?? "";
            }
            else if (disclaimerConfigurationEB.Count > 1)
            {
                disclaimer = disclaimerConfigurationEB.Where(x => x.EmployerId != null).FirstOrDefault()?.Disclaimer ?? "";
            }
            return disclaimer;
        }
        else
        {
            var disclaimerConfigurations = prePackagedPlanDisclaimersByOrigin.Where(x => (x.StateId == stateId && x.InsuranceProductId == insuranceProductId && x.Age == petAge && x.OriginId == originId) ||
                (x.StateId == null && x.InsuranceProductId == null && x.Age == petAge && x.OriginId == originId)).ToList();

            if (disclaimerConfigurations.Count == 1)
            {
                disclaimer = disclaimerConfigurations.FirstOrDefault()?.Disclaimer ?? "";
            }
            else if (disclaimerConfigurations.Count > 1)
            {
                disclaimer = disclaimerConfigurations.Where(x => x.InsuranceProductId != null && x.StateId == null).FirstOrDefault()?.Disclaimer ?? "";
            }

            return disclaimer;
        }
    }

    private async Task<List<PrePackagedPlanDto>> GetPrePackagedPlansByAge(int age, int insuranceProductId, int stateId, string? partnerGuid = null, string? employerGuid = null)
    {
        int originId = await GetOriginId(partnerGuid, false);
        var validOptions = await GetValidOptions(stateId, insuranceProductId, age, employerGuid);
        List<int> prepackagedPlans = validOptions != null ? validOptions.PrepackagedPlans : new List<int>();
        List<PrePackagedPlanExceptionsByOrigin> prePackagedPlanExceptionsByOrigins = new List<PrePackagedPlanExceptionsByOrigin>();

        var cacheKey = $"Figo.Static.Rate.PrepackagedPlanConfigurations";
        if (_prepackagedPlanConfigurationsList == null)
        {
            _prepackagedPlanConfigurationsList = await _cache.GetOrCreateAsync(cacheKey, async () => await _context.PrepackagedPlanConfigurations.AsNoTracking().Include(x => x.PrepackagedPlan).AsNoTracking().ToListAsync().ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);
        }

        var prepackagedPlanConfigurations = _prepackagedPlanConfigurationsList.Where(x => x.Age == age);

        if (_prePackagedPlanExceptionsByOriginsList == null)
        {
            cacheKey = $"Figo.Static.Rate.PrePackagedPlansExceptionsByOrigin";
            _prePackagedPlanExceptionsByOriginsList = await _cache.GetOrCreateAsync(cacheKey, async () => await _context.PrePackagedPlanExceptionsByOrigin.AsNoTracking().ToListAsync().ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);
        }

        prePackagedPlanExceptionsByOrigins = _prePackagedPlanExceptionsByOriginsList.Clone();

        var plans = prepackagedPlanConfigurations.Where(x => prepackagedPlans.Contains(x.PrepackagedPlanId)).ToList();

        foreach (var prePackagedPlan in plans)
        {
            var exceptions = prePackagedPlanExceptionsByOrigins.Where(x => x.OriginId == originId && x.PrepackagedPlanConfigurationId == prePackagedPlan.Id).FirstOrDefault();

            if (exceptions != null)
            {
                prePackagedPlan.DeductibleId = exceptions.DeductibleId;
                prePackagedPlan.ReimbursementId = exceptions.ReimbursementId;
            }

            SetPrepackagedPlanDescription(prePackagedPlan);
        }

        return _mapper.Map<List<PrepackagedPlanConfiguration>, List<PrePackagedPlanDto>>(plans);
    }

    private async Task<InsuranceStateFactor?> GetProductByStateFactor(DateTime effectiveDate, string stateAbbr, bool full = false)
    {
        if (_insuranceStateFactorList == null)
        {
            var cacheKey = $"Figo.Static.Rate.InsuranceStateFactors";
            _insuranceStateFactorList = await _cache.GetOrCreateAsync(cacheKey, async () => await _context.InsuranceStateFactors.AsNoTracking().ToListAsync().ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);
        }

        var insuranceStateFactors = _insuranceStateFactorList.Clone();

        StateProvince? state = await GetStateProvinceByAbbreviation(stateAbbr);

        if (!full)
        {
            int GPI001 = 21;
            var unQuotebleProducts = new List<int> { GPI001 };

            insuranceStateFactors = insuranceStateFactors.Where(i => !unQuotebleProducts.Contains(i.InsuranceProductId)).ToList();
        }

        int stateId = state != null ? state.Id : 0;
        return insuranceStateFactors
            .Where(i => i.StateId == stateId && effectiveDate >= i.EffectiveDate && i.IsActive)
            .OrderByDescending(i => i.EffectiveDate)
            .FirstOrDefault();
    }

    private async Task<InsuranceStateFactorEB?> GetProductByStateFactorEB(DateTime effectiveDate, string stateAbbr)
    {
        StateProvince? state = await GetStateProvinceByAbbreviation(stateAbbr);

        if (_insuranceStateFactorEBList == null)
        {
            var cacheKey = $"Figo.Static.Rate.InsuranceStateFactorsEB";
            _insuranceStateFactorEBList = await _cache.GetOrCreateAsync(cacheKey, async () => await _context.InsuranceStateFactorsEB.AsNoTracking().ToListAsync().ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);
        }

        var insuranceStateFactors = _insuranceStateFactorEBList.Clone();

        int stateId = state != null ? state.Id : 0;

        return insuranceStateFactors
            .Where(i => i.StateId == stateId && effectiveDate >= i.EffectiveDate && i.IsActive)
            .OrderByDescending(i => i.EffectiveDate)
            .FirstOrDefault();
    }

    private static string? GetProductFamlilyAsString(PetCloudProductFamily productFamily)
    {
        return Enum.GetName(typeof(PetCloudProductFamily), productFamily);
    }

    private async Task<string?> GetPromoCode(QuoteRequestLegacyDto quoteRequest)
    {
        if (!string.IsNullOrEmpty(quoteRequest.Partner.PartnerGuid))
        {
            var service = await GetPartner(quoteRequest.Partner.PartnerGuid);
            quoteRequest.groupCode = service.GetAvailablePromoCode();
        }

        return quoteRequest.groupCode;
    }

    private void GetQuoteAddFormsVersion(QuoteDto quote, PolicyImage diamondQuote, SystemData systemData)
    {
        AddFormsVersion? formsVer = systemData.AddFormsVersion.FirstOrDefault(x => x.VersionId == diamondQuote.VersionId && x.AfvStartDate <= quote.EffectiveDate && x.AfvEndDate >= quote.EffectiveDate);
        if (formsVer != null)
        {
            diamondQuote.AddFormsVersionId = formsVer.AddFormsVersionId;
        }
        else
        {
            diamondQuote.AddFormsVersionId = DEFAULT_VERSION_ID;
        }
    }

    private async Task GetQuoteAnnualTerm(PolicyImage diamondQuote)
    {
        //Diamond.Services.GetStaticData.Models.VersionData versionData = await GetCachedVersionData(diamondQuote.VersionId);
        PolicyTerm? pTerm = _versionData.PolicyTerms.FirstOrDefault(x => x.GuaranteedRatePeriodMonths == 12 && x.PolicyTermTypeId == Diamond.Services.GetStaticData.Models.PolicyTermType.Standard);
        if (pTerm != null)
        {
            diamondQuote.PolicyTermId = (int)pTerm.PolicyTermId;
        }
    }

    private void GetQuoteRatingVersion(QuoteDto quote, PolicyImage diamondQuote, SystemData systemData)
    {
        RatingVersion? rateVer = systemData.RatingVersions.FirstOrDefault(x => x.VersionId == diamondQuote.VersionId && x.StartDate.Date <= quote.EffectiveDate.Date && x.EndDate.Date >= quote.EffectiveDate.Date);
        if (rateVer != null)
        {
            diamondQuote.RatingVersionId = rateVer.RatingVersionId;
        }
        else
        {
            diamondQuote.RatingVersionId = DEFAULT_VERSION_ID;
        }
    }

    private void GetQuoteUnderwritingVersionId(QuoteDto quote, PolicyImage diamondQuote, SystemData systemData)
    {
        UnderwritingVersion? underVer = systemData.UnderwritingVersions.FirstOrDefault(x => x.VersionId == diamondQuote.VersionId && x.StartDate <= quote.EffectiveDate && x.EndDate >= quote.EffectiveDate);
        if (underVer != null)
        {
            diamondQuote.UnderwritingVersionId = underVer.UnderwritingVersionId;
        }
        else
        {
            diamondQuote.UnderwritingVersionId = DEFAULT_VERSION_ID;
        }
    }

    private async Task<PolicyImageRate> GetRateOnlyByPolicyImage(PolicyImage image)
    {
        var request = new RateOnlyRequest
        {
            PolicyImage = image
        };

        var response = await _diamondApiService.PostAsync<RateOnlyRequest, RateOnlyResponse>(request, "Policy/RateOnly");

        if (response.ResponseData == null || !response.ResponseData.Success || response.ResponseData.PolicyImage == null)
        {
            throw new FigoException("RateOnly error");
        }

        return response.ResponseData.PolicyImage;
    }

    public ValueTask<PolicyImageRate> RateOnly(PolicyImageRate pImage)
    {
        return _diamondService.RateOnly(new RateOnlyCommand { PolicyImage = pImage });
    }

    private List<int> GetReimbursementIds(List<int> optionItems, List<QuoteReimbursementDto>? reimbursements)
    {
        var reimbursementIds = new List<int>();
        QuoteReimbursementDto? reimb = null;
        double reimbVal = 0;
        for (int i = 0; i < optionItems.Count; i++)
        {
            reimbVal = (TOTAL_PERCENTAGE - Convert.ToDouble(optionItems[i])) / TOTAL_PERCENTAGE;
            reimb = reimbursements != null ? reimbursements.Where(r => r.PercentVal == reimbVal).FirstOrDefault() : null;
            if (reimb != null)
            {
                reimbursementIds.Add(reimb.Id);
            }
        }

        return reimbursementIds;
    }

    private async Task<List<QuoteReimbursementDto>> GetReimbursements(int versionId = 1)
    {
        var returnValue = new List<QuoteReimbursementDto>();
        //var versionData = await GetCachedVersionData(versionId);
        foreach (var coInsuranceType in _versionData.CoinsuranceTypes)
        {
            QuoteReimbursementDto reimbursement = new QuoteReimbursementDto
            {
                Id = coInsuranceType.CoinsuranceTypeId,
                PercentVal = CalculateReimbursementRate(coInsuranceType.Description)
            };
            reimbursement.Description = reimbursement.PercentVal.ToString("P0").Replace(" ", "");

            returnValue.Add(reimbursement);
        }

        returnValue.Sort((r1, r2) => r1.PercentVal.CompareTo(r2.PercentVal));

        return returnValue;
    }

    private async Task<StateProvince?> GetStateProvinceByAbbreviation(string abbreviation)
    {
        if (_stateProvince == null)
        {
            var cacheKey = $"Figo.Static.Rate.StateProvince.{abbreviation}";
            _stateProvince = await _cache.GetOrCreateAsync(cacheKey, async () => await _context.StateProvinces.AsNoTracking().FirstOrDefaultAsync(x => x.Abbreviation == abbreviation).ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);
        }

        return _stateProvince;
    }

    private async Task<TaxResultModel> GetTaxRatesForAddress(ZipCodeInfoDto zipCodeInfo, int stateId)
    {
        try
        {
            ReadPropertiesRecursive(zipCodeInfo);

            Diamond.Core.Models.Policy.AddressRequest address = new Diamond.Core.Models.Policy.AddressRequest();

            var taxResponse = await _diamondService.GeoTaxLookup(zipCodeInfo.City ?? "", stateId, zipCodeInfo.Code ?? "");

            return new TaxResultModel(taxResponse.Address);
        }
        catch (Exception ex)
        {
            throw new FigoException(ex.Message);
        }
    }

    private async Task<PrePackagedPlanValidOptionsByStateDto?> GetValidOptions(int stateId, int insuranceProductId, int petAge, string? employerGuid)
    {
        var packagesByAge = await GetPrePackagedPlanValidOptionsByAge(petAge).ConfigureAwait(false);
        var filteredPackages = new List<PrePackagedPlanValidOptionsByState>();
        bool isPrepackagedPlanByEmployeer = false;

        if (!string.IsNullOrEmpty(employerGuid))
        {
            filteredPackages = packagesByAge.Where(x => x.Employer?.GuID == employerGuid).ToList();
        }

        if (filteredPackages.Count == 0)
        {
            filteredPackages = packagesByAge.Where(x => x.EmployerId == null && ((x.StateId == stateId && x.InsuranceProductId == insuranceProductId && x.Age == petAge) ||
                                   (x.InsuranceProductId == null && x.StateId == null && x.Age == petAge))).ToList();
        }
        else
        {
            isPrepackagedPlanByEmployeer = true;
        }

        var validOptions = _mapper.Map<List<PrePackagedPlanValidOptionsByState>, List<PrePackagedPlanValidOptionsByStateDto>>(filteredPackages);

        if (validOptions.Count == 1)
        {
            return validOptions.FirstOrDefault();
        }
        else
        {
            if (isPrepackagedPlanByEmployeer)
            {
                return validOptions.Where(x => x.InsuranceProductId != null && x.StateId != null && x.EmployerId != null).FirstOrDefault();
            }
            else
            {
                return validOptions.Where(x => x.InsuranceProductId != null && x.StateId != null && x.EmployerId == null).FirstOrDefault();
            }
        }
    }

    private async Task<int> GetVersionId(DateTime effectiveDate, string state, int companyId, int lobId)
    {        
        _logger.LogInformation("RateService::GetVersionId - Fetching version for CompanyId: {companyId}, State: {state}, LobId: {lobId} on EffectiveDate: {effectiveDate}.", companyId, state, lobId, effectiveDate);
        
        var systemDataResponse = await GetCachedSystemData();
        var version = systemDataResponse.Versions
                         .OrderByDescending(x => x.StartDate)
                         .FirstOrDefault(x => x.State == state && x.CompanyId == companyId
                                              && x.StartDate.Date <= effectiveDate.Date && x.EndDate.Date >= effectiveDate.Date && x.LobId == lobId);

        if (version == null)
        {
            _logger.LogWarning("RateService::GetVersionId - Version not found for CompanyId: {companyId}, State: {state}, LobId: {lobId} on EffectiveDate: {effectiveDate}. Trying next day.", companyId,state, lobId, effectiveDate);

            var newEffDate = effectiveDate.AddDays(1);
            version = systemDataResponse.Versions
                         .OrderByDescending(x => x.StartDate)
                         .FirstOrDefault(x => x.State == state && x.CompanyId == companyId
                                              && x.StartDate <= newEffDate && x.EndDate >= newEffDate && x.LobId == lobId);
        }
        if (version == null)
        {
            throw new FigoException("Version data not found");
        }
        await SetVersionData(version.VersionId);
        return version != null ? version.VersionId : 0;
    }

    private async Task<ZipCodeInfoDto> GetZipCodeInfo(string zipCode)
    {
        var zipCodeInfo = await GetByZipcodeThrowWhenNULL(zipCode);

        return new ZipCodeInfoDto
        {
            Code = zipCodeInfo.ZIPCode,
            State = zipCodeInfo.StateAbbr,
            City = zipCodeInfo.CityName
        };
    }

    private static void HandleDentalTreatment(RatePetQuoteDto petQuote, ICollection<InsuranceModifierEB> insuranceModifiers, Diamond.Core.CoverageCode parent, int pmsModifierId)
    {
        var insuranceProductModifier = insuranceModifiers.Where(x => x.PMSModifierId == (int)parent).FirstOrDefault();
        var bundle = insuranceProductModifier?.BundleInsuranceModifiersEB?.Where(x => x.PMSModifierId == pmsModifierId).FirstOrDefault();
        HandleAddModifier(petQuote, bundle, true);
    }

    private void HandleModifiers(RatePetQuoteDto petQuote, ICollection<InsuranceModifierEB> insuranceModifiers)
    {
        if (petQuote.IsOpeningQuote || petQuote.IsInitialRate)
        {
            bool? isWellnessModifierSelected = null;

            if (petQuote.WellnessPlanType.HasValue && petQuote.WellnessPlanType.Value > 0)
            {
                isWellnessModifierSelected = true;
            }
            else if (petQuote.WellnessPlanType.HasValue && petQuote.WellnessPlanType.Value == 0)
            {
                isWellnessModifierSelected = false;
            }

            HandleParentModifier(petQuote, insuranceModifiers, Diamond.Core.CoverageCode.ExamFees, petQuote.VetFeesAdded);
            HandleParentModifier(petQuote, insuranceModifiers, Diamond.Core.CoverageCode.NonMedicalBenefits, petQuote.ExtraCarePackAdded);
            HandleParentModifier(petQuote, insuranceModifiers, Diamond.Core.CoverageCode.PerIncidentCopay, petQuote.PerIncidentCoPayAdded);
            HandleParentModifier(petQuote, insuranceModifiers, Diamond.Core.CoverageCode.Wellness, isWellnessModifierSelected);

            if (isWellnessModifierSelected.HasValue)
            {
                HandleWellnessBundles(petQuote, insuranceModifiers, Diamond.Core.CoverageCode.Wellness, petQuote.WellnessPlanType ?? 0);

                if (petQuote.HasDentalTreatment)
                {
                    HandleDentalTreatment(petQuote, insuranceModifiers, Diamond.Core.CoverageCode.Wellness, (int)Diamond.Core.CoverageCode.DentalTreatments);
                }
            }
        }
    }

    private static void HandleWellnessBundles(RatePetQuoteDto petQuote, ICollection<InsuranceModifierEB> insuranceModifiers, Diamond.Core.CoverageCode parent, int coverageLimitId)
    {
        var insuranceProductModifier = insuranceModifiers.Where(x => x.PMSModifierId == (int)parent).FirstOrDefault();

        if (insuranceProductModifier?.BundleInsuranceModifiersEB != null)
        {
            foreach (var bundle in insuranceProductModifier.BundleInsuranceModifiersEB)
            {
                bool selected = false;

                if (bundle.CoverageLimitId == coverageLimitId)
                {
                    selected = true;
                }

                HandleAddModifier(petQuote, bundle, selected);
            }
        }
    }

    private async Task<bool> IsExamFees(string? state)
    {
        var stateValue = state ?? "";
        var settings = await GetSocialSettings();

        var states = settings.Where(x => x.Key == INCLUDED_VET_FEES_STATES_KEY && x.IsActive).FirstOrDefault();
        return states != null ? states.Value.ToUpper().Contains(stateValue.ToUpper()) : false;
    }

    private async Task<List<Setting>> GetSocialSettings()
    {
        if (_socialSettingsList == null)
        {
            var cacheKey = $"Figo.Static.Rate.SocialSettings";
            _socialSettingsList = await _cache.GetOrCreateAsync(cacheKey, async () => await _petCloudSocialDbContext.Settings.AsNoTracking().ToListAsync().ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);
        }

        return _socialSettingsList.Clone();
    }

    private static bool IsMaskValid(string promoCode)
    {
        bool ret = false;
        if (promoCode.Length >= 11 && promoCode.Length <= 26)
        {
            ret = System.Text.RegularExpressions.Regex.IsMatch(promoCode, "[0-9][A-Za-z][0-9][A-Za-z0-9-]{5,20}[0-9][A-Za-z][0-9]");
        }
        return ret;
    }

    private static PetQuoteRateResponseDto MapEBQuoteData(QuoteResponseDto petQuoteResponseDto, RatePetQuoteDto ebPetQuote, string? promoCode, int petTypeId, int? prePackagedPlanId)
    {
        var ebPetQuoteResponseDto = new PetQuoteRateResponseDto();

        ebPetQuoteResponseDto.petQuoteId = ebPetQuote.id;
        ebPetQuoteResponseDto.cloudOrderId = ebPetQuote.cloudOrderId;
        ebPetQuoteResponseDto.promoCode = promoCode;
        ebPetQuoteResponseDto.petName = ebPetQuote.petName;
        ebPetQuoteResponseDto.petType = (PetTypes)(petTypeId - 1); // When validating breed, it internally adds +1. So we need to reset it.
        ebPetQuoteResponseDto.modifiers = ebPetQuote.modifiers;

        ebPetQuoteResponseDto.annualPremium = petQuoteResponseDto.AnnualPremium != null ? (decimal)petQuoteResponseDto.AnnualPremium : 0;
        ebPetQuoteResponseDto.breedId = petQuoteResponseDto.BreedId;
        ebPetQuoteResponseDto.breedName = petQuoteResponseDto.BreedName;
        ebPetQuoteResponseDto.Deductible = petQuoteResponseDto.Deductible;
        ebPetQuoteResponseDto.deductibleName = petQuoteResponseDto.DeductibleName;
        ebPetQuoteResponseDto.Deductibles = petQuoteResponseDto.Deductibles;
        ebPetQuoteResponseDto.gender = petQuoteResponseDto.Gender;
        ebPetQuoteResponseDto.genderName = petQuoteResponseDto.Gender.ToString();//petQuoteResponseDto.Gender == Gender.Male ? "Male" : "Female";
        ebPetQuoteResponseDto.monthlyPremium = petQuoteResponseDto.MonthlyPremium != null ? (decimal)petQuoteResponseDto.MonthlyPremium : 0;
        ebPetQuoteResponseDto.petAgeId = petQuoteResponseDto.PetAgeId;
        ebPetQuoteResponseDto.petAgeName = petQuoteResponseDto.PetAgeName;
        ebPetQuoteResponseDto.Plan = petQuoteResponseDto.Plan;
        ebPetQuoteResponseDto.planName = petQuoteResponseDto.PlanName;
        ebPetQuoteResponseDto.Plans = petQuoteResponseDto.PlansEB;
        ebPetQuoteResponseDto.Reimbursement = petQuoteResponseDto.Reimbursement;
        ebPetQuoteResponseDto.ReimbursementName = petQuoteResponseDto.ReimbursementName;
        ebPetQuoteResponseDto.Reimbursements = petQuoteResponseDto.Reimbursements;
        ebPetQuoteResponseDto.annualTaxes = petQuoteResponseDto.AnnualTaxes;
        ebPetQuoteResponseDto.monthlyTaxes = petQuoteResponseDto.MonthlyTaxes;
        ebPetQuoteResponseDto.PrePackagedPlanId = prePackagedPlanId ?? petQuoteResponseDto.PrepackagedPlanId;
        ebPetQuoteResponseDto.PrePackagedPlans = petQuoteResponseDto.PrePackagedPlans;
        ebPetQuoteResponseDto.PrepackagedPlanDisclaimer = petQuoteResponseDto.PrepackagedPlanDisclaimer;

        return ebPetQuoteResponseDto;
    }

    private async Task<QuoteResponseDto> MapQuoteDataEB(PolicyImageRate policyRated, QuoteDto quote, InsuranceProductEB product)
    {
        int versionId = policyRated.VersionId != null ? (int)policyRated.VersionId : 1;
        List<QuoteDeductibleDto> deductibles = await GetDeductibles(versionId);
        List<QuoteReimbursementDto> reimbursements = await GetReimbursements(versionId);
        List<DiscountDto> discounts = await GetDiscounts(policyRated.LOB.PolicyLevel.Modifiers, product.InsuranceModifiersEB);
        var annualTax = GetAnnualTax(policyRated.LOB.PolicyLevel.Coverages, quote?.ZipCodeInfo?.State ?? "");
        var monthlyTax = GetMonthlyTax(annualTax, quote?.ZipCodeInfo?.State ?? "");
        var dynamicModifiers = GetDynamicModifiers(policyRated, product.InsuranceModifiersEB);

        if (quote != null) { quote.VersionId = versionId; }

        var plan = quote != null ? quote.Plan : PMSPolicyPlans.NA;
        var deductible = quote != null ? quote.Deductible : PMSDeductibles.NA;
        var reimbursement = quote != null ? quote.Reimbursement : PMSReimbursements.NA;

        return new QuoteResponseDto
        {
            AnnualPremium = policyRated.FullTermPremium,
            MonthlyPremium = policyRated.FullTermPremium / 12,
            Plan = plan,
            PlanName = EnumUtil.GetEnumDescription(plan),
            Deductible = deductible,
            DeductibleName = EnumUtil.GetEnumDescription(deductible),
            Reimbursement = reimbursement,
            ReimbursementName = EnumUtil.GetEnumDescription(reimbursement),
            PlansEB = await GetPlansInformationEB(policyRated.LOB.PolicyLevel.Coverages, quote ?? new QuoteDto(), deductibles, reimbursements, product.InsuranceProductPlansEB.ToList()),
            Reimbursements = reimbursements,
            Deductibles = deductibles,
            AnnualTaxes = annualTax,
            MonthlyTaxes = monthlyTax,
            Discounts = discounts,
            DynamicModifiers = dynamicModifiers
        };
    }

    private async Task MultiplePetCheck(QuoteRequestLegacyDto quoteRequestDto)
    {
        if (quoteRequestDto.diamondClientId != 0)
        {
            var customerId = await GetCustomerIdByDiamondClientId(quoteRequestDto.diamondClientId);
            if (customerId != null)
            {
                quoteRequestDto.isMultiplePets = await GetIsMultiplePetsByCustomerId((int)customerId);
            }
        }
    }

    private async Task<QuoteResponseDto> PrepareEBQuoteResponseForMap(RatePetQuoteDto ebPetQuote, InsuranceProductEB insuranceProduct, ZipCode zipCodeInfo, string groupCode, DateTime effDate,
            bool multiplePetDiscount, bool isExamFees, BreedDto breed, int diamondClientId = 0, string? employer = null)
    {
        DateTime birthDate = GetDateOfBirth(ebPetQuote.petAge ?? "");
        Gender petGender = ebPetQuote.petSex?.ToLower() == "male" ? Diamond.Core.FigoModels.Gender.Male : Diamond.Core.FigoModels.Gender.Female;

        var quote = new QuoteDto
        {
            Plan = ebPetQuote.plan,
            Deductible = ebPetQuote.deductible,
            Reimbursement = ebPetQuote.reimbursement,
            PetName = ebPetQuote.petName,
            PetBirthDate = birthDate,
            EffectiveDate = effDate,
            PetBreedId = breed.DiamondBreedId,
            PromoCode = groupCode,
            CustomerEmail = String.Empty,
            CustomerName = String.Empty,
            Gender = petGender,
            ZipCodeInfo = new ZipCodeInfoDto
            {
                Code = zipCodeInfo.ZIPCode,
                State = zipCodeInfo.StateAbbr,
                City = zipCodeInfo.CityName
            },
            IsExamFees = isExamFees
        };

        bool applyDefaultAgeFactorsRef = false;

        SetInsuranceModifiers(ebPetQuote, insuranceProduct, employer);

        await SetInsuranceProductDefaultsEB(quote, insuranceProduct, applyDefaultAgeFactorsRef);

        QuoteResponseDto petQuoteResponse = await GetPolicyRateOnlyEB(quote, insuranceProduct, multiplePetDiscount);

        await ApplyDynamicModifiers(insuranceProduct, quote, multiplePetDiscount, petQuoteResponse);

        int petAgeYears = QuoteHelper.LoadAges().FirstOrDefault(a => a.Description == ebPetQuote.petAge)?.Years ?? 0;
        await GetPrePackagedPlanValidOptionsByAge(petAgeYears).ConfigureAwait(false);
        await SetAndGetReimbusementsAndDeductibles().ConfigureAwait(false);

        var insuranceProductMap = _mapper.Map<InsuranceProductEB, InsuranceProduct>(insuranceProduct);
        await ValidRatingOptionsFilter(petQuoteResponse, insuranceProductMap, petAgeYears, employer);

        string? employerGuid = employer;
        petQuoteResponse.PrePackagedPlans = await GetPrePackagedPlansByAge(petAgeYears, insuranceProduct.Id, insuranceProduct.SelectedStateFactorEB?.StateId ?? 0, employerGuid: employer);
        SetPlanIdToPrepackagedPlans(petQuoteResponse.PlansEB, petQuoteResponse.PrePackagedPlans);

        SetPrepackagedPlanDefaults(petQuoteResponse, ebPetQuote);

        SetPetAgeInformation(petAgeYears, petQuoteResponse);
        SetBreedInformation(breed, petQuoteResponse);
        petQuoteResponse.Gender = quote.Gender;
        petQuoteResponse.PrepackagedPlanDisclaimer = await GetPrepackagedPlanDisclaimerByOrigin(insuranceProduct.SelectedStateFactorEB?.StateId ?? 0, insuranceProduct.Id, petAgeYears, true, string.Empty, employerGuid);

        return petQuoteResponse;
    }

    private async Task<QuoteRateResponseDto> QuoteRate(QuoteRequestLegacyDto quoteRequest, bool isRate = true)
    {
        ValidatePetQuote(quoteRequest);
        var zipCode = await ValidateRateInformation(quoteRequest);
        await MultiplePetCheck(quoteRequest);

        return await CreateQuoteRate(quoteRequest, zipCode, isRate);
    }

    private async Task<QuoteRateResponseDto> QuoteRateEB(QuoteRequestLegacyDto quoteRequest, bool isRate = true)
    {
        Quote? quote = null;
        int? customerId = 0;

        if (quoteRequest.petQuotes == null || quoteRequest.petQuotes.Count == 0)
        {
            throw new FigoException("At least one pet must be added for quote.");
        }

        QuoteRateResponseDto response = new QuoteRateResponseDto();
        response.ebPetQuoteResponseList = new List<PetQuoteRateResponseDto>();

        ZipCode zipCodeInfo = await GetByZipcodeThrowWhenNULL(quoteRequest.zipCode ?? "");

        bool isExamFees = await IsExamFees(zipCodeInfo.StateAbbr);

        await MultiplePetCheck(quoteRequest);

        bool multiplePetDiscount = quoteRequest.petQuotes.Count > 1 || quoteRequest.isMultiplePets;

        InsuranceProductEB insuranceProduct = await GetInsuranceProductEB(zipCodeInfo, quoteRequest.EffectiveDate);

        response.insuranceProductEB = insuranceProduct;

        await WaiveFeesEB(quoteRequest.ebGuID ?? "", insuranceProduct).ConfigureAwait(false);

        if (quoteRequest.IsOpenQuote && !string.IsNullOrEmpty(quoteRequest.QuoteGuid))
        {
            Guid quoteGuid = Guid.Parse(quoteRequest.QuoteGuid);
            quote = await _context.Quotes.AsNoTracking().FirstOrDefaultAsync(x => x.GuidId == quoteGuid);
        }

        foreach (var ebPetQuote in quoteRequest.petQuotes)
        {
            if (quote != null && !quote.IsPurchased)
            {
                SetPowerUpsForOpenQuote(ebPetQuote, quote.Id);
            }
            else if (ebPetQuote.modifiers == null || ebPetQuote.modifiers.Count == 0)
            {
                ebPetQuote.IsInitialRate = true;
            }

            ValidateBreedDto validateBreed = BuildValidateBreedObject(ebPetQuote, insuranceProduct.ProductFamilyID, zipCodeInfo.ZIPCode);
            BreedDto breed = await ValidatePetBreed(validateBreed);

            InsuranceProductEB clonedInsuranceProductEB = insuranceProduct.Clone();

            var petQuoteResponse = await PrepareEBQuoteResponseForMap(ebPetQuote, clonedInsuranceProductEB, zipCodeInfo, quoteRequest.groupCode ?? "", quoteRequest.EffectiveDate,
                multiplePetDiscount, isExamFees, breed, quoteRequest.diamondClientId, employer: quoteRequest.ebGuID);

            petQuoteResponse.InsuranceProductEB = clonedInsuranceProductEB;

            int petTypeId = (int)breed.SpeciesId;
            var ebPetQuoteResponse = MapEBQuoteData(petQuoteResponse, ebPetQuote, quoteRequest.groupCode, petTypeId, ebPetQuote.userSelectedInfoPlan?.PrePackagedPlanId);

            ebPetQuoteResponse.InsuranceModifiersEB = DynamicModifiers(petQuoteResponse.InsuranceProductEB?.InsuranceModifiersEB ?? new List<InsuranceModifierEB>(), petQuoteResponse.DynamicModifiers ?? new List<DynamicModifierDto>());
            ebPetQuoteResponse.InsuranceModifiersEB = SetIsSelectedDiscounts(petQuoteResponse.Discounts, petQuoteResponse.InsuranceProductEB?.InsuranceModifiersEB ?? new List<InsuranceModifierEB>());

            SetIsSelectedDefaults(ebPetQuoteResponse.InsuranceModifiersEB, quoteRequest.IsOpenQuote, ebPetQuote);

            response.ebPetQuoteResponseList.Add(ebPetQuoteResponse);
        }

        response.effectiveDate = quoteRequest.effectiveDate;
        response.zipCode = quoteRequest.zipCode;
        response.groupCode = quoteRequest.groupCode;
        response.groupCodeDscr = quoteRequest.groupCodeDscr;
        response.isMultiplePets = quoteRequest.isMultiplePets;
        response.stateAbrv = zipCodeInfo.StateAbbr;

        response.CoverageInformation = await GetCoverageInformationDetails(response.insuranceProductEB.Id);

        response.SiteMessages = await GetMessageSetting(zipCodeInfo.StateAbbr, quoteRequest.EffectiveDate);

        var customerMarketChannels = await _context.CustomerMarketChannels.Include(x => x.Customer).Include(x => x.MarketingChannel).Where(x => x.Customer != null && x.Customer.Username == quoteRequest.eMail).ToListAsync();
        customerMarketChannels.ForEach(mc => response.MarketingChannels?.Add(new MarketingChannelDto { DisplayName = mc.MarketingChannel?.DisplayName, Id = mc.MarketChannelId, OriginId = mc.MarketingChannel?.OriginId }));

        response.insuranceProductEB.InsuranceModifiersEB.ToList().ForEach(item =>
        {
            item.OptionalBenefitsDetailsItem.ForEach(o =>
            {
                if (o.BulletIcon != null)
                {
                    o.BulletIcon = GetUrlImage(o.BulletIcon);
                }
            });
        });

        response.ebPetQuoteResponseList.ForEach(item =>
        {
            item.InsuranceModifiersEB?.ForEach(x =>
            {
                x.OptionalBenefitsDetailsItem.ForEach(y =>
                {
                    if (y.BulletIcon != null)
                    {
                        y.BulletIcon = GetUrlImage(y.BulletIcon);
                    }
                });
            });
        });

        return response;
    }

    private async Task<InsuranceProduct> RemoveInsuranceModifiers(bool isEB, InsuranceProduct insuranceProduct, string? partnerGuid = null)
    {
        List<InsuranceModifierByState> insuranceModifierByStates = await GetByInsuranceModifiers(isEB, insuranceProduct);

        if (insuranceModifierByStates is null || insuranceModifierByStates.Count == 0)
        {
            return insuranceProduct;
        }

        List<InsuranceModifierExceptionByStateAndPartner> exeptions = new List<InsuranceModifierExceptionByStateAndPartner>();

        if (!isEB)
        {
            PartnerConfigData? partner = partnerGuid != null ? await GetPartnerConfig(partnerGuid) : null;

            if (partner != null && partner?.Id > 0)
            {
                int partnerId = partner?.Id ?? 0;
                int stateFactorId = insuranceProduct.SelectedStateFactor?.Id ?? 0;
                exeptions = await GetInsuranceModifierExceptionsByStateAndPartner(partnerId, stateFactorId);
            }
        }

        foreach (var insuranceModifierByState in insuranceModifierByStates)
        {
            var itemToRemove = insuranceProduct.InsuranceModifiers.Where(w => w.Id == insuranceModifierByState.InsuranceModifierId).SingleOrDefault();

            if (itemToRemove != null && itemToRemove.Id > 0 && !exeptions.Any(x => x.InsuranceModifierId == itemToRemove.Id))
            {
                insuranceProduct.InsuranceModifiers.Remove(itemToRemove);

                var itemToRemoveModal = insuranceProduct.InsuranceMultiModalItems.Where(w => w.ItemLabel == itemToRemove.TitleText).SingleOrDefault();

                if (itemToRemoveModal != null && itemToRemoveModal.Id > 0)
                {
                    insuranceProduct.InsuranceMultiModalItems.Remove(itemToRemoveModal);
                }
            }
        }

        return insuranceProduct;
    }

    private async Task<List<InsuranceModifierExceptionByStateAndPartner>> GetInsuranceModifierExceptionsByStateAndPartner(int partnerId, int stateFactorId)
    {
        var cacheKey = $"Figo.Static.Rate.InsuranceModifierExceptionsByStateAndPartner.{partnerId}.{stateFactorId}";
        return await _cache.GetOrCreateAsync(cacheKey, async () => await _context.InsuranceModifierExceptionsByStateAndPartner.AsNoTracking().Where(x => x.PartnerConfigId == partnerId &&
                  x.InsuranceStateFactorId == stateFactorId &&
                  x.IsActive).ToListAsync(), TimeSpan.FromDays(365), CancellationToken.None);
    }

    private static void RemoveInsuranceModifiersDiscounts(InsuranceProductEB product, QuoteRequestLegacyDto quoteRequest)
    {
        if (quoteRequest.IsGoodDogPartner)
        {
            var itemToRemove = product.InsuranceModifiersEB.Single(m => m.InsuranceModifierTypeEBId == (int)Common.Models.Insurance.ModifierTypeEnum.DISCOUNT
                && m.PMSModifierId == (int)Modifiers.Discount10 && m.AppText == null);

            if (itemToRemove != null)
            {
                product.InsuranceModifiersEB.Remove(itemToRemove);
            }
        }
    }

    private static void RemoveInsuranceModifiersDiscounts(InsuranceProductDto product, QuoteRequestLegacyDto quoteRequest)
    {
        if (quoteRequest.IsGoodDogPartner)
        {
            var itemToRemove = product.InsuranceModifiers.Single(m => m.InsuranceModifierTypeId == (int)Common.Models.Insurance.ModifierTypeEnum.DISCOUNT
                && m.PMSModifierId == (int)Modifiers.Discount10 && m.AppText == null);

            if (itemToRemove != null)
            {
                product.InsuranceModifiers.Remove(itemToRemove);
            }
        }
    }

    private async Task<QuoteRateResponseDto> SelectedRate(QuoteRequestLegacyDto quoteRequest, bool isRate = true)
    {
        ValidatePetQuote(quoteRequest);
        var zipCode = await ValidateRateInformation(quoteRequest);
        await MultiplePetCheck(quoteRequest);

        return await CreateQuoteRateSelectedRate(quoteRequest, zipCode, isRate);
    }

    private async Task<QuoteResponseDto> SelectedRatePrepareEBQuoteResponseForMap(RatePetQuoteDto ebPetQuote, InsuranceProductEB insuranceProduct, ZipCode zipCodeInfo, string groupCode, DateTime effDate,
        bool multiplePetDiscount, bool isExamFees, BreedDto breed, int diamondClientId = 0, string? employer = null)
    {
        DateTime birthDate = GetDateOfBirth(ebPetQuote.petAge ?? "");
        Gender petGender = ebPetQuote.petSex?.ToLower() == "male" ? Diamond.Core.FigoModels.Gender.Male : Diamond.Core.FigoModels.Gender.Female;

        var quote = new QuoteDto
        {
            Plan = ebPetQuote.plan,
            Deductible = ebPetQuote.deductible,
            Reimbursement = ebPetQuote.reimbursement,
            PetName = ebPetQuote.petName,
            PetBirthDate = birthDate,
            EffectiveDate = effDate,
            PetBreedId = breed.DiamondBreedId,
            PromoCode = groupCode,
            CustomerEmail = String.Empty,
            CustomerName = String.Empty,
            Gender = petGender,
            ZipCodeInfo = new ZipCodeInfoDto
            {
                Code = zipCodeInfo.ZIPCode,
                State = zipCodeInfo.StateAbbr,
                City = zipCodeInfo.CityName
            },
            IsExamFees = isExamFees
        };

        bool applyDefaultAgeFactorsRef = false;

        SetInsuranceModifiers(ebPetQuote, insuranceProduct, employer);

        applyDefaultAgeFactorsRef = await SetInsuranceProductDefaultsEB(quote, insuranceProduct, applyDefaultAgeFactorsRef);

        QuoteResponseDto petQuoteResponse = await GetPolicyRateOnlyEB(quote, insuranceProduct, multiplePetDiscount);

        await ApplyDynamicModifiers(insuranceProduct, quote, multiplePetDiscount, petQuoteResponse);

        int petAgeYears = QuoteHelper.LoadAges().FirstOrDefault(a => a.Description == ebPetQuote.petAge)?.Years ?? 0;

        // VerifySkipAgeFactorStates
        var settings = await GetSocialSettings();
        var states = settings.FirstOrDefault(setting => setting.Key == SKIP_AGE_FACTOR_STATES_KEY);
        var applySkipAgeFactorStates = states?.Value.ToUpper().Contains(zipCodeInfo.StateAbbr.ToUpper()) == true;
        var petAgeDto = GetThirdPartyQuoteQuery.PetAgeDto.LoadAges().FirstOrDefault(a => a.Description == ebPetQuote.petAge);
        if (!applySkipAgeFactorStates && petAgeDto != null)
        {
            await AgeFactorFilterEB(petQuoteResponse, (int)breed.SpeciesId, petAgeDto.Years, insuranceProduct, applyDefaultAgeFactorsRef);
        }

        var insuranceProductMap = _mapper.Map<InsuranceProductEB, InsuranceProduct>(insuranceProduct);
        await ValidRatingOptionsFilter(petQuoteResponse, insuranceProductMap, petAgeYears, employer);

        SetPetAgeInformation(petAgeYears, petQuoteResponse);
        SetBreedInformation(breed, petQuoteResponse);
        petQuoteResponse.Gender = quote.Gender;

        return petQuoteResponse;
    }

    private static void SetBreedInformation(BreedDto breed, QuoteResponseDto response)
    {
        response.BreedId = breed.PetCloudBreedId ?? 0;
        response.BreedName = breed.PetCloudBreedName;
    }

    private async Task<Tuple<bool, List<UserSelectedModifiersDto>>> SetDefaultEBModifiers(int insuranceProductEBId, string employerGuid, List<UserSelectedModifiersDto> modifiers, RatePetQuoteDto petQuote)
    {
        if (!string.IsNullOrEmpty(employerGuid))
        {
            if (_insuranceModifierEBDefaultsByEmployersList == null)
            {
                var cacheKey = $"Figo.Static.Rate.InsuranceModifierEBDefaultsByEmployers.{insuranceProductEBId}.{employerGuid}";
                _insuranceModifierEBDefaultsByEmployersList = (await _cache.GetAsync<List<InsuranceModifierEBDefaultsByEmployer>>(cacheKey)).Value;

                if (_insuranceModifierEBDefaultsByEmployersList == null)
                {
                    _insuranceModifierEBDefaultsByEmployersList = await _context.InsuranceModifierEBDefaultsByEmployers.Include(i => i.InsuranceModifierEB).AsNoTracking().Where(x => x.InsuranceModifierEB != null && x.InsuranceModifierEB.InsuranceProductEBId == insuranceProductEBId && x.EmployerEB != null && x.EmployerEB.GuID == employerGuid).ToListAsync().ConfigureAwait(false);
                    await _cache.SetAsync(cacheKey, _insuranceModifierEBDefaultsByEmployersList, TimeSpan.FromDays(365), default);
                }
            }

            var powerUpsdefaults = _insuranceModifierEBDefaultsByEmployersList.Clone();

            if (powerUpsdefaults.Count > 0)
            {
                modifiers = new List<UserSelectedModifiersDto>();

                foreach (var pu in powerUpsdefaults)
                {
                    bool? selected = pu.IsSelected;

                    if (pu.InsuranceModifierEB?.PMSModifierId == (int)CoverageCode.NonMedicalBenefits && petQuote.ExtraCarePackAdded != null)
                    {
                        selected = petQuote.ExtraCarePackAdded;
                    }

                    modifiers.Add(new UserSelectedModifiersDto()
                    {
                        id = pu.InsuranceModifierEBId ?? 0,
                        isSelected = selected
                    });
                }

                return new Tuple<bool, List<UserSelectedModifiersDto>>(true, modifiers);
            }
        }

        return new Tuple<bool, List<UserSelectedModifiersDto>>(false, modifiers);
    }

    private static void SetDefaultsForOpenQuote(PrePackagedPlanDto? prePackagedPlan, QuoteResponseDto petQuoteResponse, RatePetQuoteDto ratePetQuoteDto)
    {
        if (ratePetQuoteDto?.userSelectedInfoPlan?.PrePackagedPlanId != ConstantsUtil.PREPACKAGEDPLAN_CUSTOMPLAN)
        {
            prePackagedPlan = petQuoteResponse?.PrePackagedPlans?.Where(x => x.Id == ratePetQuoteDto?.userSelectedInfoPlan?.PrePackagedPlanId).FirstOrDefault();
            if (prePackagedPlan != null && petQuoteResponse != null)
            {
                petQuoteResponse.Plan = (PMSPolicyPlans)prePackagedPlan.PlanId;
                petQuoteResponse.PrepackagedPlanId = prePackagedPlan.Id;
                petQuoteResponse.Deductible = (PMSDeductibles)prePackagedPlan.DeductibleId;
                petQuoteResponse.DeductibleName = petQuoteResponse.PlansEB?.SelectMany(x => x.RatingOptions).Where(y => y.DeductibleId == (int)petQuoteResponse.Deductible).FirstOrDefault()?.DeductibleName;
                petQuoteResponse.Reimbursement = (PMSReimbursements)prePackagedPlan.ReimbursementId;
                petQuoteResponse.ReimbursementName = EnumUtil.GetEnumDescription(petQuoteResponse.Reimbursement);
            }
        }
        else
        {
            petQuoteResponse.PrepackagedPlanId = prePackagedPlan?.Id ?? 0;
            petQuoteResponse.Deductible = ratePetQuoteDto.deductible;
            petQuoteResponse.DeductibleName = EnumUtil.GetEnumDescription(petQuoteResponse.Deductible);
            petQuoteResponse.Reimbursement = ratePetQuoteDto.reimbursement;
            petQuoteResponse.ReimbursementName = EnumUtil.GetEnumDescription(petQuoteResponse.Reimbursement);
        }

        if (petQuoteResponse?.PlansEB != null)
        {
            if (ratePetQuoteDto?.userSelectedInfoPlan?.PrePackagedPlanId != ConstantsUtil.PREPACKAGEDPLAN_CUSTOMPLAN)
            {
                petQuoteResponse.Plan = prePackagedPlan != null ? (PMSPolicyPlans)prePackagedPlan.PlanId : Diamond.Core.FigoModels.PMSPolicyPlans.NA;
            }

            var plan = petQuoteResponse.PlansEB.Where(x => x.Plan == petQuoteResponse.Plan).FirstOrDefault();
            var ratingOption = plan?.RatingOptions.Where(x => x.DeductibleId == (int)petQuoteResponse.Deductible && x.ReimbursementId == (int)petQuoteResponse.Reimbursement).FirstOrDefault();

            petQuoteResponse.MonthlyPremium = (double)(ratingOption?.MonthlyPremium ?? 0);
            petQuoteResponse.AnnualPremium = (double)(ratingOption?.AnnualPremium ?? 0);
        }
    }

    private static void SetDefaultsForRateFlow(PrePackagedPlanDto? prePackagedPlan, QuoteResponseDto petQuoteResponse)
    {
        prePackagedPlan = petQuoteResponse?.PrePackagedPlans?.Where(x => x.Id == ConstantsUtil.PREPACKAGEDPLAN_GOOD).FirstOrDefault();

        if (prePackagedPlan == null)
        {
            prePackagedPlan = petQuoteResponse?.PrePackagedPlans?.Where(x => x.Id == ConstantsUtil.PREPACKAGEDPLAN_MOSTPOPULARID).FirstOrDefault();

            if (prePackagedPlan == null)
            {
                prePackagedPlan = petQuoteResponse?.PrePackagedPlans?.Where(x => x.Id == ConstantsUtil.PREPACKAGEDPLAN_VALUEPLUSID).FirstOrDefault();

                if (prePackagedPlan == null)
                {
                    prePackagedPlan = petQuoteResponse?.PrePackagedPlans?.Where(x => x.Id == ConstantsUtil.PREPACKAGEDPLAN_HIGHERCOVERAGEID).FirstOrDefault();
                }
            }
        }

        if (petQuoteResponse != null)
        {
            if (prePackagedPlan != null)
            {
                var validCombination = petQuoteResponse.PlansEB?.SelectMany(x => x.RatingOptions).Where(y => y.PlanId == prePackagedPlan.PlanId && y.DeductibleId == prePackagedPlan.DeductibleId && y.ReimbursementId == prePackagedPlan.ReimbursementId).FirstOrDefault();

                petQuoteResponse.Plan = (PMSPolicyPlans)prePackagedPlan.PlanId;
                petQuoteResponse.PrepackagedPlanId = prePackagedPlan.Id;
                petQuoteResponse.Deductible = (PMSDeductibles)prePackagedPlan.DeductibleId;
                petQuoteResponse.Reimbursement = (PMSReimbursements)prePackagedPlan.ReimbursementId;
                petQuoteResponse.ReimbursementName = EnumUtil.GetEnumDescription(petQuoteResponse.Reimbursement);

                if (validCombination != null)
                {
                    petQuoteResponse.DeductibleName = validCombination.DeductibleName;
                    petQuoteResponse.AnnualPremium = Decimal.ToDouble(validCombination.AnnualPremium);
                    petQuoteResponse.MonthlyPremium = Decimal.ToDouble(validCombination.MonthlyPremium);
                }
            }
        }
    }

    private async Task SetDiscounts(List<QuoteModifierDto> modifiers, PolicyImage image, PolicyImage? rootPolicyImage, bool multiPetDiscount, PetCloudProductFamily productFamily)
    {
        image.LOB.PolicyLevel.Modifiers = new List<Diamond.Core.Models.Policy.Modifier>();

        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }

        var modifierDetails = modifiers.FirstOrDefault()?.ModifierDetails;

        var modifierDetailMultiplePetDiscount = modifierDetails?.Where(x => x.PMSModifierId == (int)ModifierTypeId.MultiplePetDiscount).FirstOrDefault();
        var modifierDetailMultiPet = modifierDetails?.Where(x => x.PMSModifierId == (int)ModifierTypeId.MultiPet).FirstOrDefault();
        var modifierDetailMultiPolicyDiscountOverride = modifierDetails?.Where(x => x.PMSModifierId == (int)ModifierTypeId.MultiPolicyDiscountOverride).FirstOrDefault();
        var modifierDetailAffinityStrat50000 = modifierDetails?.Where(x => x.PMSModifierId == (int)ModifierTypeId.AffinityStrat50000).FirstOrDefault();

        if (modifierDetailAffinityStrat50000 != null && productFamily == PetCloudProductFamily.FPI)
        {
            await AddModifier((int)ModifierTypeId.AffinityStrat50000, image, true);
        }

        if (modifierDetailMultiPolicyDiscountOverride != null && multiPetDiscount)
        {
            await AddModifier((int)ModifierTypeId.MultiPolicyDiscountOverride, image, true);
        }

        if (rootPolicyImage == null && modifierDetailMultiplePetDiscount != null && productFamily == PetCloudProductFamily.IHC)
        {
            await AddModifier((int)ModifierTypeId.MultiplePetDiscount, image, multiPetDiscount);
        }

        if (rootPolicyImage == null && modifierDetailMultiPet != null && productFamily == PetCloudProductFamily.FPI)
        {
            await AddModifier((int)ModifierTypeId.MultiPet, image, multiPetDiscount);
        }

        if (rootPolicyImage != null)
        {
            image.LOB.MultiPolicyDiscountPolicyLinks =
            [
                new Diamond.Core.Models.Policy.MultiPolicyDiscountPolicyLink
                {
                    LinkedPolicyId = rootPolicyImage.PolicyId,
                    LinkedPolicyNumber = rootPolicyImage.PolicyNumber,
                    LinkedPolicyCurrentStatusId = rootPolicyImage.PolicyStatusCodeId
                },
            ];
        }
    }

    private async Task SetInsuranceModifiers(RatePetQuoteDto petQuote, InsuranceProductEB? insuranceProductEB, string? employerGuiD = null)
    {
        if (insuranceProductEB is null)
        {
            return;
        }

        HandleModifiers(petQuote, insuranceProductEB.InsuranceModifiersEB);

        if (petQuote.IsInitialRate && !string.IsNullOrEmpty(employerGuiD))
        {
            var defaultModifiers = await SetDefaultEBModifiers(insuranceProductEB.Id, employerGuiD, petQuote.modifiers, petQuote);

            if (defaultModifiers.Item1)
            {
                var defaultModifiersToAdd = petQuote.modifiers.Where(x => defaultModifiers.Item2.Select(y => y.id).Contains(x.id));

                if (defaultModifiersToAdd != null && defaultModifiersToAdd.Count() != 0)
                {
                    petQuote.modifiers.RemoveAll(x => defaultModifiers.Item2.Select(y => y.id).Contains(x.id));
                    var defaultModifiersCopy = defaultModifiers.Item2.Clone();

                    defaultModifiersCopy.ForEach(x =>
                    {
                        petQuote.modifiers.Add(x);
                    });
                }
            }
        }

        ConfigureModifiers(petQuote, insuranceProductEB.InsuranceModifiersEB);
    }

    private async Task<bool> SetInsuranceProductDefaults(QuoteDto quote, InsuranceProduct? product)
    {
        _versionData = await GetCachedVersionData(quote.VersionId);
        bool applyDefaultAgeFactors = false;
        int PMSCompanyId = product?.SelectedStateFactor?.PMSCompanyId ?? 0;
        int PMSLOBId = product?.SelectedStateFactor?.PMSLOBId ?? 0;
        string state = quote.ZipCodeInfo?.State ?? "";
        if (quote != null)
        {
            int versionId = await GetVersionId(quote.EffectiveDate, state, PMSCompanyId, PMSLOBId);
            quote.PMSLobId = PMSLOBId;
            quote.PMSCompanyId = PMSCompanyId;
            quote.VersionId = versionId;
        }

        if (quote?.Plan == PMSPolicyPlans.NA)
        {
            var PMSDefaultCoverageLimitId = product?.InsurancePolicyConfigurations?.FirstOrDefault()?.PMSDefaultCoverageLimitId;
            quote.Plan = PMSDefaultCoverageLimitId != null ? (PMSPolicyPlans)PMSDefaultCoverageLimitId : quote.Plan;
        }

        if (quote != null && !await ValidPlan(quote.Plan, quote.VersionId))
        {
            throw new FigoException("Invalid Plan.");
        }

        if (quote != null && quote.Deductible == PMSDeductibles.NA)
        {
            applyDefaultAgeFactors = true;
            var PMSDefaultDeductibleId = product?.InsurancePolicyConfigurations?.FirstOrDefault()?.PMSDefaultDeductibleId;
            quote.Deductible = PMSDefaultDeductibleId != null ? (PMSDeductibles)PMSDefaultDeductibleId : quote.Deductible;
        }

        if (quote != null && !await ValidDeductible(quote.Deductible, quote.VersionId))
        {
            throw new FigoException("Invalid Deductible.");
        }

        if (quote?.Reimbursement == PMSReimbursements.NA)
        {
            applyDefaultAgeFactors = true;
            var PMSDefaultReimbursementId = product?.InsurancePolicyConfigurations?.FirstOrDefault()?.PMSDefaultReimbursementId;
            quote.Reimbursement = PMSDefaultReimbursementId != null ? (PMSReimbursements)PMSDefaultReimbursementId : quote.Reimbursement;
        }

        if (quote != null && !await ValidReimbursement(quote.Reimbursement, quote.VersionId))
        {
            throw new FigoException("Invalid Reimbursement.");
        }

        return applyDefaultAgeFactors;
    }

    private async Task<bool> SetInsuranceProductDefaultsEB(QuoteDto quote, InsuranceProductEB product, bool applyDefaultAgeFactors)
    {
        int versionId = await GetVersionId(quote.EffectiveDate, quote.ZipCodeInfo?.State ?? "", product.SelectedStateFactorEB?.PMSCompanyId ?? 0, product.SelectedStateFactorEB?.PMSLOBId ?? 0);
        quote.PMSLobId = product.SelectedStateFactorEB?.PMSLOBId ?? 0;
        quote.PMSCompanyId = product.SelectedStateFactorEB?.PMSCompanyId ?? 0;
        quote.VersionId = versionId;

        if (quote.Plan == PMSPolicyPlans.NA)
        {
            var policyConfigurationEB = product.InsurancePolicyConfigurationsEB.FirstOrDefault();
            quote.Plan = policyConfigurationEB != null ? (PMSPolicyPlans)policyConfigurationEB.PMSDefaultCoverageLimitId : PMSPolicyPlans.NA;
        }
        if (!(await ValidPlan(quote.Plan, versionId)))
        {
            throw new FigoException("Invalid Plan.");
        }

        if (quote.Deductible == PMSDeductibles.NA)
        {
            applyDefaultAgeFactors = true;
            var policyConfigurationEB = product.InsurancePolicyConfigurationsEB.FirstOrDefault();
            quote.Deductible = policyConfigurationEB != null ? (PMSDeductibles)policyConfigurationEB.PMSDefaultDeductibleId : PMSDeductibles.NA;
        }
        if (!(await ValidDeductible(quote.Deductible, versionId)))
        {
            throw new FigoException("Invalid Deductible.");
        }

        if (quote.Reimbursement == PMSReimbursements.NA)
        {
            applyDefaultAgeFactors = true;
            var policyConfigurationEB = product.InsurancePolicyConfigurationsEB.FirstOrDefault();
            quote.Reimbursement = policyConfigurationEB != null ? (PMSReimbursements)policyConfigurationEB.PMSDefaultReimbursementId : PMSReimbursements.NA;
        }
        if (!(await ValidReimbursement(quote.Reimbursement, versionId)))
        {
            throw new FigoException("Invalid Reimbursement.");
        }
        return applyDefaultAgeFactors;
    }

    private static void SetIsSelectedDefaults(ICollection<InsuranceModifierEB> insuranceModifier, bool isOpenQuote, RatePetQuoteDto ratePetQuoteDto)
    {
        var coverages = insuranceModifier.Where(x => x.IsVisible && x.InsuranceModifierTypeEBId == (int)ModifierTypeEnum.COVERAGE);

        foreach (var coverage in coverages)
        {
            if (isOpenQuote)
            {
                coverage.IsSelected = null;

                if (coverage.PMSModifierId == (int)Modifiers.VetFeesIncluded && (ratePetQuoteDto?.VetFeesAdded.HasValue ?? false))
                {
                    coverage.IsSelected = ratePetQuoteDto.VetFeesAdded.Value;
                }
                else if (coverage.PMSModifierId == (int)Modifiers.ExtraCarePack && (ratePetQuoteDto?.ExtraCarePackAdded.HasValue ?? false))
                {
                    coverage.IsSelected = ratePetQuoteDto.ExtraCarePackAdded.Value;
                }
                else if (coverage.PMSModifierId == (int)Modifiers.Wellness && (ratePetQuoteDto?.WellnessPlanType.HasValue ?? false))
                {
                    coverage.IsSelected = ratePetQuoteDto.WellnessPlanType.Value > 0;
                }
                else if (coverage.PMSModifierId == (int)Modifiers.PetProtectCopay)
                {
                    coverage.IsSelected = ratePetQuoteDto?.PerIncidentCoPayAdded;
                }
            }
            else
            {
                var userCoverage = ratePetQuoteDto?.modifiers?.Where(x => x.id == coverage.Id).FirstOrDefault();

                if (userCoverage == null || !userCoverage.isSelected.HasValue)
                {
                    coverage.IsSelected = null;
                }
            }
        }
    }

    private static void SetIsSelectedDefaults(ICollection<InsuranceModifier> insuranceModifier, bool isOpenQuote, RatePetQuoteDto ratePetQuoteDto)
    {
        var coverages = insuranceModifier.Where(x => x.IsVisible && x.InsuranceModifierTypeId == (int)ModifierTypeEnum.COVERAGE);

        foreach (var coverage in coverages)
        {
            if (isOpenQuote)
            {
                coverage.IsSelected = null;

                if (coverage.PMSModifierId == (int)Modifiers.VetFeesIncluded && ratePetQuoteDto.VetFeesAdded.HasValue)
                {
                    coverage.IsSelected = ratePetQuoteDto.VetFeesAdded.Value;
                }
                else if (coverage.PMSModifierId == (int)Modifiers.ExtraCarePack && ratePetQuoteDto.ExtraCarePackAdded.HasValue)
                {
                    coverage.IsSelected = ratePetQuoteDto.ExtraCarePackAdded.Value;
                }
                else if (coverage.PMSModifierId == (int)Modifiers.Wellness && ratePetQuoteDto.WellnessPlanType.HasValue)
                {
                    coverage.IsSelected = ratePetQuoteDto.WellnessPlanType.Value > 0;
                }
                else if (coverage.PMSModifierId == (int)Modifiers.PetProtectCopay)
                {
                    coverage.IsSelected = ratePetQuoteDto.PerIncidentCoPayAdded;
                }
            }
            else
            {
                var userCoverage = ratePetQuoteDto.modifiers?.Where(x => x.id == coverage.Id).FirstOrDefault();

                if (userCoverage == null || !userCoverage.isSelected.HasValue)
                {
                    coverage.IsSelected = null;
                }
            }
        }
    }

    private static ICollection<InsuranceModifierEB> SetIsSelectedDiscounts(List<DiscountDto>? discounts, ICollection<InsuranceModifierEB> insuranceModifierEB)
    {
        if (discounts is null || discounts.Count == 0)
        {
            return insuranceModifierEB;
        }

        foreach (var insuranceModifier in insuranceModifierEB.Where(w => w.InsuranceModifierTypeEBId == (int)ModifierTypeEnum.DISCOUNT))
        {
            if (discounts.Exists(w => w.Id == insuranceModifier.PMSModifierId.GetValueOrDefault()))
            {
                insuranceModifier.IsSelected = true;
                insuranceModifier.InsuranceModifierDiscount = discounts.Where(w => w.Id == insuranceModifier.PMSModifierId.GetValueOrDefault()).First().InsuranceModifierDiscount;
            }
        }

        return insuranceModifierEB;
    }

    private async Task<bool> SetModifierOption(Diamond.Core.Models.Policy.Modifier modifier, int versionId, string optionStr)
    {
        bool returnValue = false;
        modifier.ModifierOptionDescription = optionStr;
        try
        {
            //Diamond.Services.GetStaticData.Models.VersionData versionData = await GetCachedVersionData(versionId);
            ModifierOption? modOpt = _versionData.ModifierOptions.Where(x => x.ModifierGroupId == modifier.ModifierGroupId && x.ModifierLevelId == modifier.ModifierLevelId && x.ModifierTypeId == modifier.ModifierTypeId).FirstOrDefault(x => x.Description.ToUpper() == optionStr.ToUpper());
            if (modOpt != null)
            {
                returnValue = true;
                modifier.ModifierOptionId = modOpt.ModifierOptionId;
            }
            else
            {
                modifier.ModifierOptionId = 1;
            }
        }
        catch
        {
            returnValue = false;
            modifier.ModifierOptionId = 1;
        }
        return returnValue;
    }

    private static void SetPetAgeInformation(int petAgeYears, QuoteResponseDto response)
    {
        var petAges = QuoteHelper.LoadAges();
        if (petAges.Count > 0)
        {
            PetAge? petAgeInfo = petAges.Where(p => p.Years == petAgeYears).FirstOrDefault();
            if (petAgeInfo != null)
            {
                response.PetAgeId = petAgeInfo.Id;
                response.PetAgeName = petAgeInfo.Description;
            }
        }
    }

    private static void SetPlanIdToPrepackagedPlans(List<QuotePlanDtoEB>? Plans, List<PrePackagedPlanDto> PrePackagedPlans)
    {
        if (Plans != null)
        {
            foreach (var plan in PrePackagedPlans)
            {
                var plans = Plans.Where(x => plan.PMSCoverageLimitIds.Contains((int)x.Plan)).SelectMany(x => x.RatingOptions).Where(y => y.DeductibleId == plan.DeductibleId && y.ReimbursementId == plan.ReimbursementId);

                if (plans.Count() != 1)
                {
                    throw new Exception("More than one plan was found for the default configuration.");
                }

                plan.PlanId = plans.FirstOrDefault()?.PlanId ?? 0;
            }
        }
    }

    private async Task SetPowerUpsForOpenQuote(RatePetQuoteDto petQuote, int quoteId)
    {
        var quoteItem = await _context.QuoteItems.AsNoTracking().FirstOrDefaultAsync(x => x.QuoteId == quoteId && x.PetName == petQuote.petName).ConfigureAwait(false);

        if (quoteItem != null)
        {
            petQuote.IsOpeningQuote = true;

            petQuote.VetFeesAdded = quoteItem.VetFeesAdded;
            petQuote.ExtraCarePackAdded = quoteItem.ExtraCarePackAdded;
            petQuote.PerIncidentCoPayAdded = quoteItem.PerIncidentCoPayAdded ?? false;
            petQuote.WellnessPlanType = quoteItem.WellnessPlanType;
            petQuote.HasDentalTreatment = quoteItem.HasDentalTreatment;

            var deductiblesAndReimbursements = await SetAndGetReimbusementsAndDeductibles();
            var deductibleValue = deductiblesAndReimbursements.Item1.Where(x => x.Value == quoteItem.Deductible).FirstOrDefault();
            var reimbursementValue = deductiblesAndReimbursements.Item2.Where(x => x.Value == quoteItem.Reimbursement).FirstOrDefault();

            petQuote.deductible = quoteItem.PrePackagedPlanId == ConstantsUtil.PREPACKAGEDPLAN_CUSTOMPLAN ? deductibleValue != null ? (PMSDeductibles)deductibleValue.Id : PMSDeductibles.NA : PMSDeductibles.NA;
            petQuote.reimbursement = quoteItem.PrePackagedPlanId == ConstantsUtil.PREPACKAGEDPLAN_CUSTOMPLAN ? reimbursementValue != null ? (PMSReimbursements)reimbursementValue.Id : PMSReimbursements.NA : PMSReimbursements.NA;
            petQuote.userSelectedInfoPlan = new UserSelectedInfoPlanDto
            {
                PrePackagedPlanId = quoteItem.PrePackagedPlanId
            };
        }
    }

    private void SetPrepackagedPlanDefaults(QuoteResponseDto petQuoteResponse, RatePetQuoteDto ratePetQuoteDto)
    {
        PrePackagedPlanDto prePackagedPlan = new PrePackagedPlanDto()
        {
            Id = ConstantsUtil.PREPACKAGEDPLAN_CUSTOMPLAN
        };

        if (ratePetQuoteDto != null &&
            ratePetQuoteDto.IsOpeningQuote &&
            ratePetQuoteDto.userSelectedInfoPlan != null)
        {
            SetDefaultsForOpenQuote(prePackagedPlan, petQuoteResponse, ratePetQuoteDto);
        }
        else
        {
            SetDefaultsForRateFlow(prePackagedPlan, petQuoteResponse);
        }
    }

    private static void SetPrepackagedPlanDefaultsForGetSelectedRate(QuoteResponseDto petQuoteResponse, RatePetQuoteDto ratePetQuoteDto)
    {
        var validCombination = petQuoteResponse.PlansEB?.SelectMany(x => x.RatingOptions).Where(y => y.PlanId == (int)ratePetQuoteDto.plan && y.DeductibleId == (int)ratePetQuoteDto.deductible && y.ReimbursementId == (int)ratePetQuoteDto.reimbursement).FirstOrDefault();

        if (validCombination != null)
        {
            petQuoteResponse.Plan = ratePetQuoteDto.plan;
            petQuoteResponse.Deductible = ratePetQuoteDto.deductible;
            petQuoteResponse.DeductibleName = validCombination.DeductibleName;
            petQuoteResponse.Reimbursement = ratePetQuoteDto.reimbursement;
            petQuoteResponse.ReimbursementName = EnumUtil.GetEnumDescription(petQuoteResponse.Reimbursement);
            petQuoteResponse.AnnualPremium = (double)validCombination.AnnualPremium;
            petQuoteResponse.MonthlyPremium = (double)validCombination.MonthlyPremium;
        }
    }

    private async Task SetPrepackagedPlanDescription(PrepackagedPlanConfiguration plan)
    {
        var deductiblesAndReimbursements = await SetAndGetReimbusementsAndDeductibles();
        var deductible = deductiblesAndReimbursements.Item1.Where(x => x.Id == plan.DeductibleId).FirstOrDefault();
        var reimbursement = deductiblesAndReimbursements.Item2.Where(x => x.Id == plan.ReimbursementId).FirstOrDefault();

        switch (plan.PrepackagedPlanId)
        {
            case ConstantsUtil.PREPACKAGEDPLAN_MOSTPOPULARID:
            case ConstantsUtil.PREPACKAGEDPLAN_VALUEPLUSID:
            case ConstantsUtil.PREPACKAGEDPLAN_GOOD:
            case ConstantsUtil.PREPACKAGEDPLAN_GREAT:
                plan.PrepackagedPlan.Description = string.Format(plan.PrepackagedPlan.Description,
                                                                 reimbursement?.Description,
                                                                 plan.PrepackagedPlan.AnnualLimit,
                                                                 deductible?.Value.ToString("$#,#"));
                break;

            default:
                plan.PrepackagedPlan.Description = string.Format(plan.PrepackagedPlan.Description,
                                                                 reimbursement?.Description,
                                                                 deductible?.Value.ToString("$#,#"));
                break;
        }
    }

    private async Task<Tuple<List<Deductible>, List<Reimbursement>>> SetAndGetReimbusementsAndDeductibles()
    {
        var cacheKey = $"Figo.Static.Rate.Deductibles";

        if (_deductiblesList == null)
        {
            _deductiblesList = (await _cache.GetOrCreateAsync(cacheKey, async () => await _policyContext.Deductibles.AsNoTracking().ToListAsync().ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None));
        }

        var deductibles = _deductiblesList.Clone();

        if (_reimbursementList == null)
        {
            cacheKey = $"Figo.Static.Reimbursements";
            _reimbursementList = (await _cache.GetOrCreateAsync(cacheKey, async () => await _policyContext.Reimbursements.AsNoTracking().ToListAsync().ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None));
        }

        var reimbursements = _reimbursementList.Clone();

        return new Tuple<List<Deductible>, List<Reimbursement>>(deductibles, reimbursements);
    }

    private async Task SetQuoteCoveragesEB(QuoteDto quote, PolicyImage diamondQuote, InsuranceProductEB product)
    {
        await SetQuotePetCoverage(quote, diamondQuote);
        List<InsurancePolicyDefaultCoverageEB>? coverages = product.InsurancePolicyConfigurationsEB.FirstOrDefault()?.InsurancePolicyDefaultCoveragesEB.Where(co => co.IsActive).ToList();

        if (coverages != null)
        {
            foreach (var coverage in coverages)
            {
                diamondQuote.LOB.PolicyLevel.Coverages.Add(new Diamond.Core.Models.Policy.Coverage
                {
                    CoverageCodeID = coverage.InsurancePolicyCoverageTypeEB?.PMSCoverageCodeId ?? 0,
                    Checkbox = coverage.IsChecked,
                    CoverageNum = new Diamond.Core.Models.Policy.CoverageNum { InternalValue = Guid.NewGuid().ToString() }
                });
            }
        }
    }

    private async Task SetQuoteModifiersEB(string promoCodes, PolicyImage diamondQuote, InsuranceProductEB product, bool multiplePetDiscount)
    {
        List<QuoteModifierDto> modifiers = GetInsuranceProductModifiersEB(product);

        if (modifiers.Count > 0)
        {
            var modifierList = modifiers.Where(y => y.ModifierType == (int)ModifierTypeEnum.DISCOUNT).ToList();

            await ApplyPromocodes(modifierList, diamondQuote, product.ProductFamilyID, promoCodes);

            await AddOrUpdateModifiers(modifiers.Where(y => y.ModifierType != (int)ModifierTypeEnum.DISCOUNT).ToList(), diamondQuote);

            await SetDiscounts(modifierList, diamondQuote, null, multiplePetDiscount, product.ProductFamilyID);
        }
    }

    private async Task SetQuotePetCoverage(QuoteDto quote, PolicyImage diamondQuote)
    {
        var coverage = new Diamond.Core.Models.Policy.Coverage
        {
            CoverageCodeID = (int)CoverageCode.PetCoverage,
            CoverageNum = new Diamond.Core.Models.Policy.CoverageNum { InternalValue = Guid.NewGuid().ToString() }
        };
        coverage.CoverageLimitId = (int)quote.Plan;
        coverage.DeductibleId = quote.Deductible > (PMSDeductibles)DEFAULT_MINIMUM_INT ? (int)quote.Deductible : (int)DeductibleId.Deductible_200;
        coverage.DetailStatusCode = DEFAULT_STATUS_CODE;
        coverage.CoverageDetail = new Diamond.Core.Models.Policy.CoverageDetail
        {
            CoverageTypeId = quote.IsCat ? (int)Diamond.Core.SpeciesTypeId.Cat : (int)Diamond.Core.SpeciesTypeId.Dog
        };
        coverage.CoverageDetail.CoinsuranceTypeId = quote.Reimbursement > (PMSReimbursements)DEFAULT_MINIMUM_INT ? (int)quote.Reimbursement : (int)CoInsuranceTypeId.Percent_20;
        coverage.CoverageDetail.NameInformation = quote.PetName;
        //Diamond.Services.GetStaticData.Models.BreedType? breedType = (await GetCachedVersionData(diamondQuote.VersionId)).BreedTypes.FirstOrDefault(x => x.BreedTypeId == quote.PetBreedId);
        var breedType = _versionData.BreedTypes.FirstOrDefault(x => x.BreedTypeId == quote.PetBreedId);
        coverage.CoverageDetail.BreedTypeId = breedType != null ? breedType.BreedTypeId : DEFAULT_BREED_ID;
        coverage.CoverageDetail.RetroactiveDate = new Diamond.Core.Models.Policy.RetroactiveDate { DateTime = quote.PetBirthDate };
        coverage.CoverageDetail.ZoneTypeId = (int)(quote.Gender == (Gender)DEFAULT_GENDER_FEMALE ? Diamond.Core.FigoModels.Gender.Female : Diamond.Core.FigoModels.Gender.Male);

        diamondQuote.LOB.PolicyLevel.Coverages.Add(coverage);
    }

    private void SetQuotePolicyHolderAddress(QuoteDto quote, PolicyImage diamondQuote, StateData? diamondState)
    {
        diamondQuote.PolicyHolder = new Diamond.Core.Models.Policy.PolicyHolder
        {
            Address = new Diamond.Core.Models.Policy.Address()
        };
        if (diamondState != null)
        {
            diamondQuote.PolicyHolder.Address.StateId = diamondState.StateId;
        }
        diamondQuote.PolicyHolder.Address.Zip = quote.ZipCodeInfo?.Code;
        diamondQuote.PolicyHolder.Address.City = ADDRESS_CITY;
        diamondQuote.PolicyHolder.Address.StreetName = ADDRESS_STREET_NAME;
        diamondQuote.PolicyHolder.Address.HouseNumber = ADDRESS_HOUSE_NUMBER;
    }

    private async Task<PolicyImage> SetQuoteTaxExceptions(QuoteDto quote, PolicyImage diamondQuote, InsuranceProductEB product, int stateId)
    {
        if (quote.ZipCodeInfo?.State == KY)
        {
            var taxModel = await GetTaxRatesForAddress(quote.ZipCodeInfo, stateId);
            taxModel.Address.Zip = $"{taxModel.Address.Zip}-0000";
            var GetDiamToken = await _diamondService.GetDiamToken();
            diamondQuote.TransactionUsersId = GetDiamToken.DiamondSecurityToken.DiamUserId;
            diamondQuote.BillingAddressee = new Diamond.Core.Models.Policy.BillingAddressee() { Address = taxModel.Address };
            diamondQuote.PolicyHolder.Address = taxModel.Address;
            return await SubmitApplication(diamondQuote, product.SelectedStateFactorEB?.PMSCompanyId ?? 0, product.SelectedStateFactorEB?.PMSLOBId ?? 0);
        }

        return diamondQuote;
    }

    private async Task<string?> ShortPromoCode(string? promoCode)
    {
        if (!String.IsNullOrEmpty(promoCode) && promoCode.Length > 5)
        {
            var cacheKey = $"Figo.Static.Rate.PromoCode.{promoCode}";
            var query = await _context.PromoCodes.AsNoTracking().FirstOrDefaultAsync(x => x.PromoCode == promoCode || x.HashCode == promoCode).ConfigureAwait(false);

            if(query != null)
            {
                var promoData = await _cache.GetOrCreateAsync(cacheKey, async () => query, TimeSpan.FromDays(365), CancellationToken.None);
                return promoData != null ? promoData.PromoCode : promoCode;
            }
        }
        return promoCode;
    }

    private async Task<PolicyImage> SubmitApplication(PolicyImage diamondQuote, int companyId, int lobId)
    {
        try
        {
            string text = JsonConvert.SerializeObject(diamondQuote, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });

            string filteredText = text.Replace("\"IsNew\":false", "\"IsNew\":true").Replace("\"InternalFlags\":0", "\"InternalFlags\":15").Replace("\"InternalFlags\":14", "\"InternalFlags\":15");

            PolicyImage? diamondQuoteClone = JsonConvert.DeserializeObject<PolicyImage>(filteredText);

            var request = new SubmitApplicationRequest
            {
                AlreadyValidated = true,
                IsQuote = true,
                PolicyImage = diamondQuoteClone ?? diamondQuote,
                Rate = false,
                ReturnImage = true,
                SubmitVersion = new Diamond.Core.Models.Policy.SubmitVersion
                {
                    CompanyId = companyId,
                    StateId = diamondQuote.PolicyHolder.Address.StateId,
                    LOBId = lobId,
                    TransEffDate = new Diamond.Core.Models.InsDateTime(diamondQuote.EffectiveDate.DateTime),
                    TransTypeId = (int)TransType.NewPolicy,
                    GuaranteedRatePeriodEffDate = new Diamond.Core.Models.InsDateTime(diamondQuote.EffectiveDate.DateTime)
                }
            };

            var response = await _diamondService.SubmitApplication(request);

            if (response != null && response.Success)
            {
                return response.Image;
            }
            else
            {
                throw new FigoException("SubmitApplication Error");
            }
        }
        catch (Exception ex)
        {
            throw new FigoException(ex.Message);
        }
    }

    private async Task<BreedDto> ValidatePetBreed(RatePetQuoteDto petQuote, QuoteRequestLegacyDto quoteRequest, PetCloudProductFamily productFamily)
    {
        ValidateBreedDto validateBreed = BuildValidateBreedObject(petQuote, productFamily, quoteRequest.zipCode ?? "");

        return await ValidatePetBreed(validateBreed);
    }

    private async Task<int> ValidatePetBreed(RatePetQuoteDto petQuote, QuoteRateResponseDto? quoteRateResponse, ZipCode zipCodeInfo)
    {
        ValidateBreedDto validateBreed = BuildValidateBreedObject(petQuote,
            quoteRateResponse?.insuranceProduct?.ProductFamilyID ?? PetCloudProductFamily.None, zipCodeInfo.ZIPCode);

        BreedDto breed = await ValidatePetBreed(validateBreed);
        petQuote.petBreedId = breed.PetCloudBreedId ?? 0;
        return (int)breed.SpeciesId;
    }

    private async Task<BreedDto> ValidatePetBreed(ValidateBreedDto validateBreedDto)
    {
        if (_pCBreedProductsList == null)
        {
            string? productFamilyAsString = GetProductFamlilyAsString(validateBreedDto.ProductFamily);
            var cacheKey = $"Figo.Static.Rate.PCBreedProductsByFamily.{productFamilyAsString}";

            _pCBreedProductsList = (await _cache.GetAsync<List<PCBreedProduct>>(cacheKey)).Value;

            if (_pCBreedProductsList == null)
            {
                _pCBreedProductsList = await _context.PCBreedProducts.AsNoTracking().Include(x => x.PCBreed).Where(x => x.ProductType == productFamilyAsString
                              && x.ProductType != null
                              && x.PMSBreedId > default(int)).ToListAsync();
                await _cache.SetAsync(cacheKey, _pCBreedProductsList, TimeSpan.FromDays(365), default);
            }
        }

        var pcBreedProductsByFamily = _pCBreedProductsList.Clone();

        if (validateBreedDto.BreedId != null && validateBreedDto.BreedId > 0)
        {
            pcBreedProductsByFamily = pcBreedProductsByFamily?.Where(x => x?.PCBreed.Id == validateBreedDto.BreedId).ToList();
        }
        else
        {
            pcBreedProductsByFamily = pcBreedProductsByFamily?.Where(x => x?.PCBreed.Name == validateBreedDto.BreedName).ToList();
        }

        BreedDto breed = _mapper.Map<BreedDto>(pcBreedProductsByFamily?.FirstOrDefault());

        ValidateBreed(validateBreedDto, breed);

        return breed;
    }

    private async Task<ZipCode> ValidateRateInformation(QuoteRequestLegacyDto quoteRequestDto)
    {
        return await GetByZipcodeThrowWhenNULL(quoteRequestDto.zipCode ?? "");
    }

    private async Task<bool> ValidDeductible(PMSDeductibles deductible, int versionId)
    {
        List<QuoteDeductibleDto> deductibles = await GetDeductibles(versionId);
        return deductibles.FirstOrDefault(p => p.Id == (int)deductible) != null;
    }

    private async Task<bool> ValidPlan(PMSPolicyPlans plan, int versionId)
    {
        List<PlanDto> plans = await GetCoveragesLimit(versionId);
        return plans.FirstOrDefault(p => p.Id == (int)plan) != null;
    }

    private static bool ValidPMSModifiers(IList<QuoteModifierDetailDto>? modifierDetails, PetCloudProductFamily family)
    {
        var modifierGroupCode = modifierDetails?.Where(x => x.PMSModifierId == (int)ModifierTypeId.GroupCode).FirstOrDefault();
        var modifierAffinityStrat40049999 = modifierDetails?.Where(x => x.PMSModifierId == (int)ModifierTypeId.AffinityStrat40049999).FirstOrDefault();
        var modifierAffinityStrat50000 = modifierDetails?.Where(x => x.PMSModifierId == (int)ModifierTypeId.AffinityStrat50000).FirstOrDefault();

        bool isGroupCode = modifierGroupCode != null;
        bool validAffinityStrat40049999 = modifierAffinityStrat40049999 != null;
        bool validAffinityStrat50000 = modifierAffinityStrat50000 != null && family == PetCloudProductFamily.FPI;

        if (isGroupCode || validAffinityStrat40049999 || validAffinityStrat50000)
        {
            return true;
        }

        return false;
    }

    private async Task ValidRatingOptionsFilter(QuoteResponseDto response, InsuranceProduct? insuranceProduct, int petAge, string? employerGuid = null, string? partnerGuid = null)
    {
        var validOptions = await GetInsuranceProductDedReimbValidOptions(insuranceProduct?.SelectedStateFactor?.StateId ?? 0, insuranceProduct?.Id ?? 0, petAge, employerGuid, partnerGuid);

        if (validOptions != null)
        {
            validOptions.Deductibles = GetDeductibleIds(validOptions.Deductibles, response.Deductibles);
            validOptions.Reimbursements = GetReimbursementIds(validOptions.Reimbursements, response.Reimbursements);
        }

        var exceptions = insuranceProduct?.InsuranceProductDedReimbExceptions.Where(x => x.PMSCoverageLimitId != null && x.PMSCoverageLimitId != 0).ToList();

        if (await _featureManager.IsEnabledAsync(Feature.NewAnnualCoverageOptions))
        {
            if (validOptions?.PMSCoverageLimits != null && validOptions.PMSCoverageLimits.Any())
            {
                response.PlansEB = response?.PlansEB?.Where(x => validOptions.PMSCoverageLimits.Contains((int)x.Plan)).ToList();
            }
        }
        else
        {
            var disabledPlans = response?.PlansEB?.Where(x => x.FilteredByState).ToList();

            if(disabledPlans != null && disabledPlans.Any())
            {
                foreach (var plan in disabledPlans)
                {
                    response?.PlansEB?.Remove(plan);
                }
            }
        }

        if (response?.PlansEB != null)
        {
            response.PlansEB = FilterRatingOptions(response.PlansEB, validOptions, exceptions);

            response.PlansEB.ForEach(x =>
            {
                x.RatingOptions = x.RatingOptions.OrderBy(y => y.DeductiblValue).ThenBy(z => z.ReimbursementValue).ToList();
            });
        }
    }

    private async Task<bool> ValidReimbursement(PMSReimbursements reimbursement, int versionId)
    {
        List<QuoteReimbursementDto> reimbursements = await GetReimbursements(versionId);
        return reimbursements.FirstOrDefault(p => p.Id == (int)reimbursement) != null;
    }

    private async Task WaiveFeesEB(string ebGuID, InsuranceProductEB insuranceProductEB)
    {
        EmployerEB? employer = await GetEmployerByGuid(ebGuID);

        if (employer is null)
        {
            throw new FigoException("EmployerEB information not found.");
        }

        if (_insuranceWaiveFeeEBList == null)
        {
            var cacheKey = $"Figo.Static.Rate.InsuranceWaiveFeesEB.{employer.Id}";
            _insuranceWaiveFeeEBList = await _cache.GetOrCreateAsync(cacheKey, async () => await _context.InsuranceWaiveFeesEB.AsNoTracking().Where(w => w.EmployerEBId == employer.Id).ToListAsync().ConfigureAwait(false), TimeSpan.FromDays(365), CancellationToken.None);
        }

        insuranceProductEB.InsuranceProductFeeEB.Remove(item => _insuranceWaiveFeeEBList.Any(a => a.InsuranceFeeEBId == item.InsuranceFeeEBId));
    }
}
