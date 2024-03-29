﻿using Grand.Core.Domain.Common;
using Grand.Core.Domain.Customers;
using Grand.Core.Domain.Directory;
using Grand.Core.Domain.Localization;
using Grand.Core.Domain.Orders;
using Grand.Framework.Extensions;
using Grand.Services.Catalog;
using Grand.Services.Common;
using Grand.Services.Customers;
using Grand.Services.Directory;
using Grand.Services.Helpers;
using Grand.Services.Localization;
using Grand.Services.Logging;
using Grand.Services.Messages;
using Grand.Services.Orders;
using Grand.Web.Areas.Admin.Extensions;
using Grand.Web.Areas.Admin.Interfaces;
using Grand.Web.Areas.Admin.Models.Common;
using Grand.Web.Areas.Admin.Models.Orders;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Grand.Web.Areas.Admin.Services
{
    public partial class ReturnRequestViewModelService : IReturnRequestViewModelService
    {
        #region Fields

        private readonly IOrderService _orderService;
        private readonly IProductService _productService;
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly ICustomerService _customerService;
        private readonly ILocalizationService _localizationService;
        private readonly IWorkflowMessageService _workflowMessageService;
        private readonly LocalizationSettings _localizationSettings;
        private readonly ICustomerActivityService _customerActivityService;
        private readonly IReturnRequestService _returnRequestService;
        private readonly IPriceFormatter _priceFormatter;
        private readonly ICurrencyService _currencyService;
        private readonly AddressSettings _addressSettings;
        private readonly OrderSettings _orderSettings;
        private readonly ICountryService _countryService;
        private readonly IStateProvinceService _stateProvinceService;
        private readonly IAddressAttributeService _addressAttributeService;
        private readonly IAddressAttributeParser _addressAttributeParser;
        #endregion Fields

        #region Constructors

        public ReturnRequestViewModelService(IOrderService orderService,
            IProductService productService,
            ICustomerService customerService, IDateTimeHelper dateTimeHelper,
            ILocalizationService localizationService,
            IWorkflowMessageService workflowMessageService, LocalizationSettings localizationSettings,
            ICustomerActivityService customerActivityService,
            IReturnRequestService returnRequestService,
            IPriceFormatter priceFormatter,
            ICurrencyService currencyService,
            AddressSettings addressSettings,
            ICountryService countryService,
            IStateProvinceService stateProvinceService,
            IAddressAttributeService addressAttributeService,
            IAddressAttributeParser addressAttributeParser,
            OrderSettings orderSettings)
        {
            this._orderService = orderService;
            this._productService = productService;
            this._customerService = customerService;
            this._dateTimeHelper = dateTimeHelper;
            this._localizationService = localizationService;
            this._workflowMessageService = workflowMessageService;
            this._localizationSettings = localizationSettings;
            this._customerActivityService = customerActivityService;
            this._returnRequestService = returnRequestService;
            this._priceFormatter = priceFormatter;
            this._currencyService = currencyService;
            this._addressSettings = addressSettings;
            this._countryService = countryService;
            this._stateProvinceService = stateProvinceService;
            this._addressAttributeService = addressAttributeService;
            this._addressAttributeParser = addressAttributeParser;
            this._orderSettings = orderSettings;
        }

        #endregion

        public virtual async Task<ReturnRequestModel> PrepareReturnRequestModel(ReturnRequestModel model,
            ReturnRequest returnRequest, bool excludeProperties)
        {
            if (model == null)
                throw new ArgumentNullException("model");

            if (returnRequest == null)
                throw new ArgumentNullException("returnRequest");

            var order = await _orderService.GetOrderById(returnRequest.OrderId);
            decimal unitPriceInclTaxInCustomerCurrency = 0;
            foreach (var item in returnRequest.ReturnRequestItems)
            {
                var orderItem = order.OrderItems.Where(x => x.Id == item.OrderItemId).First();
                unitPriceInclTaxInCustomerCurrency += _currencyService.ConvertCurrency(orderItem.UnitPriceInclTax, order.CurrencyRate) * item.Quantity;
            }

            model.Total = _priceFormatter.FormatPrice(unitPriceInclTaxInCustomerCurrency);
            model.Quantity = returnRequest.ReturnRequestItems.Sum(x => x.Quantity);
            model.Id = returnRequest.Id;
            model.OrderId = order.Id;
            model.OrderNumber = order.OrderNumber;
            model.ReturnNumber = returnRequest.ReturnNumber;
            model.CustomerId = returnRequest.CustomerId;
            model.NotifyCustomer = returnRequest.NotifyCustomer;
            var customer = await _customerService.GetCustomerById(returnRequest.CustomerId);
            if (customer != null)
                model.CustomerInfo = customer.IsRegistered() ? customer.Email : _localizationService.GetResource("Admin.Customers.Guest");
            else
                model.CustomerInfo = _localizationService.GetResource("Admin.Customers.Guest");

            model.ReturnRequestStatusStr = returnRequest.ReturnRequestStatus.ToString();
            model.CreatedOn = _dateTimeHelper.ConvertToUserTime(returnRequest.CreatedOnUtc, DateTimeKind.Utc);
            model.PickupDate = returnRequest.PickupDate;

            if (!excludeProperties)
            {
                var addr = new AddressModel();
                model.PickupAddress = await PrepareAddressModel(addr, returnRequest.PickupAddress, excludeProperties);
                model.CustomerComments = returnRequest.CustomerComments;
                model.StaffNotes = returnRequest.StaffNotes;
                model.ReturnRequestStatusId = returnRequest.ReturnRequestStatusId;
            }

            return model;
        }
        public virtual async Task<(IList<ReturnRequestModel> returnRequestModels, int totalCount)> PrepareReturnRequestModel(ReturnReqestListModel model, int pageIndex, int pageSize)
        {
            string customerId = string.Empty;
            if (!string.IsNullOrEmpty(model.SearchCustomerEmail))
            {
                var customer = await _customerService.GetCustomerByEmail(model.SearchCustomerEmail.ToLowerInvariant());
                if (customer != null)
                    customerId = customer.Id;
                else
                    customerId = "00000000-0000-0000-0000-000000000000";
            }
            DateTime? startDateValue = (model.StartDate == null) ? null
                : (DateTime?)_dateTimeHelper.ConvertToUtcTime(model.StartDate.Value, _dateTimeHelper.CurrentTimeZone);

            DateTime? endDateValue = (model.EndDate == null) ? null
                : (DateTime?)_dateTimeHelper.ConvertToUtcTime(model.EndDate.Value, _dateTimeHelper.CurrentTimeZone);

            var returnRequests = await _returnRequestService.SearchReturnRequests(model.StoreId,
                customerId,
                "",
                (model.SearchReturnRequestStatusId >= 0 ? (ReturnRequestStatus?)model.SearchReturnRequestStatusId : null),
                pageIndex - 1,
                pageSize,
                startDateValue,
                endDateValue);
            var returnRequestModels = new List<ReturnRequestModel>();
            foreach (var rr in returnRequests)
            {
                var rrmodel = new ReturnRequestModel();
                returnRequestModels.Add(await PrepareReturnRequestModel(rrmodel, rr, true));
            }
            return (returnRequestModels, returnRequests.TotalCount);
        }
        public virtual async Task<AddressModel> PrepareAddressModel(AddressModel model, Address address, bool excludeProperties)
        {
            if (address != null)
            {
                if (!excludeProperties)
                {
                    model = await address.ToModel(_countryService, _stateProvinceService);
                }
            }

            if (model == null)
                model = new AddressModel();

            model.FirstNameEnabled = true;
            model.FirstNameRequired = true;
            model.LastNameEnabled = true;
            model.LastNameRequired = true;
            model.EmailEnabled = true;
            model.EmailRequired = true;
            model.CompanyEnabled = _addressSettings.CompanyEnabled;
            model.CompanyRequired = _addressSettings.CompanyRequired;
            model.VatNumberEnabled = _addressSettings.VatNumberEnabled;
            model.VatNumberRequired = _addressSettings.VatNumberRequired;
            model.CountryEnabled = _addressSettings.CountryEnabled;
            model.StateProvinceEnabled = _addressSettings.StateProvinceEnabled;
            model.CityEnabled = _addressSettings.CityEnabled;
            model.CityRequired = _addressSettings.CityRequired;
            model.StreetAddressEnabled = _addressSettings.StreetAddressEnabled;
            model.StreetAddressRequired = _addressSettings.StreetAddressRequired;
            model.StreetAddress2Enabled = _addressSettings.StreetAddress2Enabled;
            model.StreetAddress2Required = _addressSettings.StreetAddress2Required;
            model.ZipPostalCodeEnabled = _addressSettings.ZipPostalCodeEnabled;
            model.ZipPostalCodeRequired = _addressSettings.ZipPostalCodeRequired;
            model.PhoneEnabled = _addressSettings.PhoneEnabled;
            model.PhoneRequired = _addressSettings.PhoneRequired;
            model.FaxEnabled = _addressSettings.FaxEnabled;
            model.FaxRequired = _addressSettings.FaxRequired;
            //countries
            model.AvailableCountries.Add(new SelectListItem { Text = _localizationService.GetResource("Admin.Address.SelectCountry"), Value = "" });
            foreach (var c in await _countryService.GetAllCountries(showHidden: true))
                model.AvailableCountries.Add(new SelectListItem { Text = c.Name, Value = c.Id.ToString(), Selected = (c.Id == model.CountryId) });
            //states
            var states = !String.IsNullOrEmpty(model.CountryId) ? await _stateProvinceService.GetStateProvincesByCountryId(model.CountryId, showHidden: true) : new List<StateProvince>();
            if (states.Count > 0)
            {
                foreach (var s in states)
                    model.AvailableStates.Add(new SelectListItem { Text = s.Name, Value = s.Id.ToString(), Selected = (s.Id == model.StateProvinceId) });
            }
            else
                model.AvailableStates.Add(new SelectListItem { Text = _localizationService.GetResource("Admin.Address.OtherNonUS"), Value = "" });
            //customer attribute services
            await model.PrepareCustomAddressAttributes(address, _addressAttributeService, _addressAttributeParser);

            return model;
        }

        public virtual async Task NotifyCustomer(ReturnRequest returnRequest)
        {
            var order = await _orderService.GetOrderById(returnRequest.OrderId);
            int queuedEmailId = await _workflowMessageService.SendReturnRequestStatusChangedCustomerNotification(returnRequest, order, _localizationSettings.DefaultAdminLanguageId);

        }
        public virtual ReturnReqestListModel PrepareReturnReqestListModel()
        {
            var model = new ReturnReqestListModel {
                //Return request status
                ReturnRequestStatus = ReturnRequestStatus.Pending.ToSelectList().ToList()
            };
            model.ReturnRequestStatus.Insert(0, new SelectListItem { Text = _localizationService.GetResource("Admin.Common.All"), Value = "-1" });

            return model;
        }
        public virtual async Task<IList<ReturnRequestModel.ReturnRequestItemModel>> PrepareReturnRequestItemModel(string returnRequestId)
        {
            var returnRequest = await _returnRequestService.GetReturnRequestById(returnRequestId);
            List<ReturnRequestModel.ReturnRequestItemModel> items = new List<ReturnRequestModel.ReturnRequestItemModel>();
            var order = await _orderService.GetOrderById(returnRequest.OrderId);

            foreach (var item in returnRequest.ReturnRequestItems)
            {
                var orderItem = order.OrderItems.Where(x => x.Id == item.OrderItemId).FirstOrDefault();

                items.Add(new ReturnRequestModel.ReturnRequestItemModel {
                    ProductId = orderItem.ProductId,
                    ProductName = (await _productService.GetProductByIdIncludeArch(orderItem.ProductId)).Name,
                    Quantity = item.Quantity,
                    UnitPrice = _priceFormatter.FormatPrice(orderItem.UnitPriceInclTax),
                    ReasonForReturn = item.ReasonForReturn,
                    RequestedAction = item.RequestedAction
                });
            }
            return items;
        }
        public virtual async Task<ReturnRequest> UpdateReturnRequestModel(ReturnRequest returnRequest, ReturnRequestModel model, string customAddressAttributes)
        {
            returnRequest.CustomerComments = model.CustomerComments;
            returnRequest.StaffNotes = model.StaffNotes;
            returnRequest.ReturnRequestStatusId = model.ReturnRequestStatusId;
            returnRequest.UpdatedOnUtc = DateTime.UtcNow;
            if (_orderSettings.ReturnRequests_AllowToSpecifyPickupDate)
                returnRequest.PickupDate = model.PickupDate;
            if (_orderSettings.ReturnRequests_AllowToSpecifyPickupAddress)
            {
                returnRequest.PickupAddress = model.PickupAddress.ToEntity();
                if (returnRequest.PickupAddress != null)
                    returnRequest.PickupAddress.CustomAttributes = customAddressAttributes;
            }
            returnRequest.NotifyCustomer = model.NotifyCustomer;
            await _returnRequestService.UpdateReturnRequest(returnRequest);
            //activity log
            await _customerActivityService.InsertActivity("EditReturnRequest", returnRequest.Id, _localizationService.GetResource("ActivityLog.EditReturnRequest"), returnRequest.Id);

            if (model.NotifyCustomer)
                await NotifyCustomer(returnRequest);
            return returnRequest;
        }
        public virtual async Task DeleteReturnRequest(ReturnRequest returnRequest)
        {
            await _returnRequestService.DeleteReturnRequest(returnRequest);
            //activity log
            await _customerActivityService.InsertActivity("DeleteReturnRequest", returnRequest.Id, _localizationService.GetResource("ActivityLog.DeleteReturnRequest"), returnRequest.Id);
        }
    }
}
