using Com.DanLiris.Service.Purchasing.Lib.Enums;
using Com.DanLiris.Service.Purchasing.Lib.Helpers;
using Com.DanLiris.Service.Purchasing.Lib.Helpers.ReadResponse;
using Com.DanLiris.Service.Purchasing.Lib.Interfaces;
using Com.DanLiris.Service.Purchasing.Lib.Models.BankExpenditureNoteModel;
using Com.DanLiris.Service.Purchasing.Lib.Models.Expedition;
using Com.DanLiris.Service.Purchasing.Lib.Models.UnitPaymentOrderModel;
using Com.DanLiris.Service.Purchasing.Lib.Services;
using Com.DanLiris.Service.Purchasing.Lib.ViewModels.BankExpenditureNote;
using Com.DanLiris.Service.Purchasing.Lib.ViewModels.IntegrationViewModel;
using Com.DanLiris.Service.Purchasing.Lib.ViewModels.NewIntegrationViewModel;
//using Com.DanLiris.Service.Purchasing.Lib.ViewModels.IntegrationViewModel;
using Com.Moonlay.Models;
using Com.Moonlay.NetCore.Lib;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Com.DanLiris.Service.Purchasing.Lib.Facades.BankExpenditureNoteFacades
{
    public class BankExpenditureNoteFacade : IBankExpenditureNoteFacade, IReadByIdable<BankExpenditureNoteModel>
    {
        private const string V = "Operasional";
        private readonly PurchasingDbContext dbContext;
        private readonly DbSet<BankExpenditureNoteModel> dbSet;
        private readonly DbSet<BankExpenditureNoteDetailModel> detailDbSet;
        private readonly DbSet<UnitPaymentOrder> unitPaymentOrderDbSet;
        private readonly IBankDocumentNumberGenerator bankDocumentNumberGenerator;
        public readonly IServiceProvider serviceProvider;

        private readonly string USER_AGENT = "Facade";
        private readonly string CREDITOR_ACCOUNT_URI = "creditor-account/bank-expenditure-note/list";

        public BankExpenditureNoteFacade(PurchasingDbContext dbContext, IBankDocumentNumberGenerator bankDocumentNumberGenerator, IServiceProvider serviceProvider)
        {
            this.dbContext = dbContext;
            this.bankDocumentNumberGenerator = new BankDocumentNumberGenerator(dbContext);
            dbSet = dbContext.Set<BankExpenditureNoteModel>();
            detailDbSet = dbContext.Set<BankExpenditureNoteDetailModel>();
            unitPaymentOrderDbSet = dbContext.Set<UnitPaymentOrder>();
            this.serviceProvider = serviceProvider;
        }

        public async Task<BankExpenditureNoteModel> ReadById(int id)
        {
            return await this.dbContext.BankExpenditureNotes
                .AsNoTracking()
                    .Include(p => p.Details)
                        .ThenInclude(p => p.Items)
                .Where(d => d.Id.Equals(id) && d.IsDeleted.Equals(false))
                .FirstOrDefaultAsync();
        }

        public ReadResponse<object> Read(int Page = 1, int Size = 25, string Order = "{}", string Keyword = null, string Filter = "{}")
        {
            IQueryable<BankExpenditureNoteModel> Query = this.dbSet;

            var queryItems = Query.Select(x => x.Details.Select(y => y.Items).ToList());

            Query = Query
                .Select(s => new BankExpenditureNoteModel
                {
                    Id = s.Id,
                    CreatedUtc = s.CreatedUtc,
                    LastModifiedUtc = s.LastModifiedUtc,
                    BankName = s.BankName,
                    BankAccountName = s.BankAccountName,
                    BankAccountNumber = s.BankAccountNumber,
                    DocumentNo = s.DocumentNo,
                    SupplierName = s.SupplierName,
                    GrandTotal = s.GrandTotal,
                    BankCurrencyCode = s.BankCurrencyCode,
                    CurrencyRate = s.CurrencyRate,
                    IsPosted = s.IsPosted,
                    Date = s.Date,
                    Details = s.Details.Where(x => x.BankExpenditureNoteId == s.Id).Select(a => new BankExpenditureNoteDetailModel
                    {
                        SupplierName = a.SupplierName,
                        UnitPaymentOrderNo = a.UnitPaymentOrderNo,
                        Items = a.Items.Where(b => b.BankExpenditureNoteDetailId == a.Id).ToList()
                    }).ToList()
                });

            List<string> searchAttributes = new List<string>()
            {
                "DocumentNo", "BankName", "SupplierName","BankCurrencyCode"
            };

            Query = QueryHelper<BankExpenditureNoteModel>.ConfigureSearch(Query, searchAttributes, Keyword);

            Dictionary<string, string> FilterDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(Filter);
            Query = QueryHelper<BankExpenditureNoteModel>.ConfigureFilter(Query, FilterDictionary);

            Dictionary<string, string> OrderDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(Order);
            Query = QueryHelper<BankExpenditureNoteModel>.ConfigureOrder(Query, OrderDictionary);

            Pageable<BankExpenditureNoteModel> pageable = new Pageable<BankExpenditureNoteModel>(Query, Page - 1, Size);
            List<BankExpenditureNoteModel> Data = pageable.Data.ToList();

            List<object> list = new List<object>();
            list.AddRange(
               Data.Select(s => new
               {
                   s.Id,
                   s.DocumentNo,
                   s.CreatedUtc,
                   s.BankName,
                   s.BankAccountName,
                   s.BankAccountNumber,
                   s.SupplierName,
                   s.GrandTotal,
                   s.BankCurrencyCode,
                   s.IsPosted,
                   s.Date,
                   Details = s.Details.Select(sl => new { sl.SupplierName, sl.UnitPaymentOrderNo, sl.Items }).ToList(),
               }).ToList()
            );

            int TotalData = pageable.TotalCount;

            return new ReadResponse<object>(list, TotalData, OrderDictionary);
        }

        public async Task<int> Update(int id, BankExpenditureNoteModel model, IdentityService identityService)
        {
            int Updated = 0;
            string username = identityService.Username;
            using (var transaction = this.dbContext.Database.BeginTransaction())
            {
                try
                {
                    EntityExtension.FlagForUpdate(model, username, USER_AGENT);
                    dbContext.Entry(model).Property(x => x.GrandTotal).IsModified = true;
                    dbContext.Entry(model).Property(x => x.BGCheckNumber).IsModified = true;
                    dbContext.Entry(model).Property(x => x.LastModifiedAgent).IsModified = true;
                    dbContext.Entry(model).Property(x => x.LastModifiedBy).IsModified = true;
                    dbContext.Entry(model).Property(x => x.LastModifiedUtc).IsModified = true;

                    foreach (var detail in model.Details)
                    {
                        if (detail.Id == 0)
                        {
                            EntityExtension.FlagForCreate(detail, username, USER_AGENT);
                            dbContext.BankExpenditureNoteDetails.Add(detail);

                            PurchasingDocumentExpedition pde = new PurchasingDocumentExpedition
                            {
                                Id = (int)detail.UnitPaymentOrderId,
                                IsPaid = true,
                                BankExpenditureNoteNo = model.DocumentNo,
                                BankExpenditureNoteDate = model.Date
                            };

                            EntityExtension.FlagForUpdate(pde, username, USER_AGENT);
                            //dbContext.Attach(pde);
                            dbContext.Entry(pde).Property(x => x.IsPaid).IsModified = true;
                            dbContext.Entry(pde).Property(x => x.BankExpenditureNoteNo).IsModified = true;
                            dbContext.Entry(pde).Property(x => x.BankExpenditureNoteDate).IsModified = true;
                            dbContext.Entry(pde).Property(x => x.LastModifiedAgent).IsModified = true;
                            dbContext.Entry(pde).Property(x => x.LastModifiedBy).IsModified = true;
                            dbContext.Entry(pde).Property(x => x.LastModifiedUtc).IsModified = true;

                            foreach (var item in detail.Items)
                            {
                                EntityExtension.FlagForCreate(item, username, USER_AGENT);
                            }
                        }
                    }

                    foreach (var detail in dbContext.BankExpenditureNoteDetails.AsNoTracking().Where(p => p.BankExpenditureNoteId == model.Id))
                    {
                        BankExpenditureNoteDetailModel detailModel = model.Details.FirstOrDefault(prop => prop.Id.Equals(detail.Id));

                        if (detailModel == null)
                        {
                            EntityExtension.FlagForDelete(detail, username, USER_AGENT);

                            foreach (var item in dbContext.BankExpenditureNoteItems.AsNoTracking().Where(p => p.BankExpenditureNoteDetailId == detail.Id))
                            {
                                EntityExtension.FlagForDelete(item, username, USER_AGENT);
                                dbContext.BankExpenditureNoteItems.Update(item);
                            }

                            dbContext.BankExpenditureNoteDetails.Update(detail);

                            PurchasingDocumentExpedition pde = new PurchasingDocumentExpedition
                            {
                                Id = (int)detail.UnitPaymentOrderId,
                                IsPaid = false,
                                BankExpenditureNoteNo = null,
                                BankExpenditureNoteDate = null
                            };

                            EntityExtension.FlagForUpdate(pde, username, USER_AGENT);
                            //dbContext.Attach(pde);
                            dbContext.Entry(pde).Property(x => x.IsPaid).IsModified = true;
                            dbContext.Entry(pde).Property(x => x.BankExpenditureNoteNo).IsModified = true;
                            dbContext.Entry(pde).Property(x => x.BankExpenditureNoteDate).IsModified = true;
                            dbContext.Entry(pde).Property(x => x.LastModifiedAgent).IsModified = true;
                            dbContext.Entry(pde).Property(x => x.LastModifiedBy).IsModified = true;
                            dbContext.Entry(pde).Property(x => x.LastModifiedUtc).IsModified = true;
                        }
                    }

                    Updated = await dbContext.SaveChangesAsync();
                    //DeleteDailyBankTransaction(model.DocumentNo, identityService);
                    //CreateDailyBankTransaction(model, identityService);
                    //UpdateCreditorAccount(model, identityService);
                    //ReverseJournalTransaction(model);
                    //CreateJournalTransaction(model, identityService);
                    transaction.Commit();
                }
                catch (Exception e)
                {
                    transaction.Rollback();
                    throw new Exception(e.Message);
                }
            }

            return Updated;
        }

        public async Task<int> Create(BankExpenditureNoteModel model, IdentityService identityService)
        {
            int Created = 0;
            string username = identityService.Username;
            TimeSpan timeOffset = new TimeSpan(identityService.TimezoneOffset, 0, 0);
            using (var transaction = dbContext.Database.BeginTransaction())
            {
                try
                {
                    EntityExtension.FlagForCreate(model, username, USER_AGENT);

                    model.DocumentNo = await bankDocumentNumberGenerator.GenerateDocumentNumber("K", model.BankCode, username,model.Date.ToOffset(timeOffset).Date);

                    if (model.BankCurrencyCode != "IDR")
                    {
                        var garmentCurrency = await GetGarmentCurrency(model.CurrencyCode);
                        model.CurrencyRate = garmentCurrency.Rate.GetValueOrDefault();
                    }

                    foreach (var detail in model.Details)
                    {
                        EntityExtension.FlagForCreate(detail, username, USER_AGENT);

                        PurchasingDocumentExpedition pde = new PurchasingDocumentExpedition
                        {
                            Id = (int)detail.UnitPaymentOrderId,
                            IsPaid = true,
                            BankExpenditureNoteNo = model.DocumentNo,
                            BankExpenditureNoteDate = model.Date
                        };

                        EntityExtension.FlagForUpdate(pde, username, USER_AGENT);
                        dbContext.Attach(pde);
                        dbContext.Entry(pde).Property(x => x.IsPaid).IsModified = true;
                        dbContext.Entry(pde).Property(x => x.BankExpenditureNoteNo).IsModified = true;
                        dbContext.Entry(pde).Property(x => x.BankExpenditureNoteDate).IsModified = true;
                        dbContext.Entry(pde).Property(x => x.LastModifiedAgent).IsModified = true;
                        dbContext.Entry(pde).Property(x => x.LastModifiedBy).IsModified = true;
                        dbContext.Entry(pde).Property(x => x.LastModifiedUtc).IsModified = true;

                        foreach (var item in detail.Items)
                        {
                            EntityExtension.FlagForCreate(item, username, USER_AGENT);
                        }
                    }

                    dbSet.Add(model);
                    Created = await dbContext.SaveChangesAsync();
                    //await CreateJournalTransaction(model, identityService);
                    //CreateDailyBankTransaction(model, identityService);
                    //CreateCreditorAccount(model, identityService);
                    transaction.Commit();
                }
                catch (Exception e)
                {
                    transaction.Rollback();
                    throw new Exception(e.Message);
                }
            }

            return Created;
        }

        private async Task CreateJournalTransaction(BankExpenditureNoteModel model, IdentityService identityService)
        {
            //var unitPaymentOrderIds = model.Details.Select(detail => detail.UnitPaymentOrderId).ToList();
            //var unitPaymentOrders = dbContext.UnitPaymentOrders.Where(unitPaymentOrder => unitPaymentOrderIds.Contains(unitPaymentOrder.Id)).ToList();
            var items = new List<JournalTransactionItem>();
            foreach (var detail in model.Details)
            {
                //var unitPaymentOrder = unitPaymentOrders.FirstOrDefault(entity => entity.Id == detail.UnitPaymentOrderId);
                var sumDataByUnit = detail.Items.GroupBy(g => g.UnitCode).Select(s => new
                {
                    UnitCode = s.Key,
                    Total = s.Sum(sm => sm.Price * model.CurrencyRate)
                });



                foreach (var datum in sumDataByUnit)
                {
                    var item = new JournalTransactionItem()
                    {
                        COA = new COA()
                        {
                            Code = COAGenerator.GetDebtCOA(model.SupplierImport, detail.DivisionName, datum.UnitCode)
                        },
                        //Debit = Convert.ToDecimal(datum.Total + (datum.Total * 0.1)),
                        Debit = Convert.ToDecimal(datum.Total),
                        Remark = detail.UnitPaymentOrderNo + " / " + detail.InvoiceNo
                    };

                    var vatCOA = "";
                    if (detail.Vat > 0)
                    {
                        if (model.SupplierImport)
                        {
                            vatCOA = "1510.00." + COAGenerator.GetDivisionAndUnitCOACode(detail.DivisionName, datum.UnitCode);
                        }
                        else
                        {
                            vatCOA = "1509.00." + COAGenerator.GetDivisionAndUnitCOACode(detail.DivisionName, datum.UnitCode);
                        }
                    }

                    //if (!string.IsNullOrWhiteSpace(vatCOA))
                    //{
                    //    item.Debit += Convert.ToDecimal(datum.Total * 0.1);
                    //}

                    items.Add(item);
                }
            }

            //items = items.GroupBy(g => g.COA.Code).Select(s => new JournalTransactionItem()
            //{
            //    COA = s.First().COA,
            //    Debit = s.Sum(sm => Math.Round(sm.Debit.GetValueOrDefault(), 4))
            //}).ToList();

            var bankJournalItem = new JournalTransactionItem()
            {
                COA = new COA()
                {
                    Code = model.BankAccountCOA
                },
                //Credit = items.Sum(s => Math.Round(s.Debit.GetValueOrDefault(), 4))
                Credit = items.Sum(s => s.Debit.GetValueOrDefault())
            };
            items.Add(bankJournalItem);

            var modelToPost = new JournalTransaction()
            {
                Date = model.Date,
                Description = "Bukti Pengeluaran Bank",
                ReferenceNo = model.DocumentNo,
                Items = items
            };

            string journalTransactionUri = "journal-transactions";
            //var httpClient = new HttpClientService(identityService);
            var httpClient = (IHttpClientService)serviceProvider.GetService(typeof(IHttpClientService));
            var response = await httpClient.PostAsync($"{APIEndpoint.Finance}{journalTransactionUri}", new StringContent(JsonConvert.SerializeObject(modelToPost).ToString(), Encoding.UTF8, General.JsonMediaType));
            response.EnsureSuccessStatusCode();
        }

        private async Task<GarmentCurrency> GetGarmentCurrency(string codeCurrency)
        {
            string date = DateTimeOffset.UtcNow.ToString("yyyy/MM/dd HH:mm:ss");
            string queryString = $"code={codeCurrency}&stringDate={date}";

            var http = serviceProvider.GetService<IHttpClientService>();
            var response = await http.GetAsync(APIEndpoint.Core + $"master/garment-currencies/single-by-code-date?{queryString}");

            var responseString = await response.Content.ReadAsStringAsync();
            var jsonSerializationSetting = new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Ignore };

            var result = JsonConvert.DeserializeObject<APIDefaultResponse<GarmentCurrency>>(responseString, jsonSerializationSetting);

            return result.data;
        }

        //private void ReverseJournalTransaction(BankExpenditureNoteModel model)
        //{
        //    foreach (var detail in model.Details)
        //    {
        //        //string journalTransactionUri = $"journal-transactions/reverse-transactions/{model.DocumentNo + "/" + detail.UnitPaymentOrderNo}";
        //        string journalTransactionUri = $"journal-transactions/reverse-transactions/{model.DocumentNo}";
        //        var httpClient = (IHttpClientService)serviceProvider.GetService(typeof(IHttpClientService));
        //        var response = httpClient.PostAsync($"{APIEndpoint.Finance}{journalTransactionUri}", new StringContent(JsonConvert.SerializeObject(new object()).ToString(), Encoding.UTF8, General.JsonMediaType)).Result;
        //        response.EnsureSuccessStatusCode();
        //    }
        //}

        public async Task<int> Delete(int Id, IdentityService identityService)
        {
            int Count = 0;
            string username = identityService.Username;

            if (dbSet.Count(p => p.Id == Id && p.IsDeleted == false).Equals(0))
            {
                return 0;
            }

            using (var transaction = dbContext.Database.BeginTransaction())
            {
                try
                {
                    BankExpenditureNoteModel bankExpenditureNote = dbContext.BankExpenditureNotes.Include(entity => entity.Details).Single(p => p.Id == Id);

                    ICollection<BankExpenditureNoteDetailModel> Details = new List<BankExpenditureNoteDetailModel>(dbContext.BankExpenditureNoteDetails.Where(p => p.BankExpenditureNoteId.Equals(Id)));

                    foreach (var detail in Details)
                    {
                        ICollection<BankExpenditureNoteItemModel> Items = new List<BankExpenditureNoteItemModel>(dbContext.BankExpenditureNoteItems.Where(p => p.BankExpenditureNoteDetailId.Equals(detail.Id)));

                        foreach (var item in Items)
                        {
                            EntityExtension.FlagForDelete(item, username, USER_AGENT);
                            dbContext.BankExpenditureNoteItems.Update(item);
                        }

                        EntityExtension.FlagForDelete(detail, username, USER_AGENT);
                        dbContext.BankExpenditureNoteDetails.Update(detail);

                        PurchasingDocumentExpedition pde = new PurchasingDocumentExpedition
                        {
                            Id = (int)detail.UnitPaymentOrderId,
                            IsPaid = false,
                            BankExpenditureNoteNo = null,
                            BankExpenditureNoteDate = null
                        };

                        EntityExtension.FlagForUpdate(pde, username, USER_AGENT);
                        //dbContext.Attach(pde);
                        dbContext.Entry(pde).Property(x => x.IsPaid).IsModified = true;
                        dbContext.Entry(pde).Property(x => x.BankExpenditureNoteNo).IsModified = true;
                        dbContext.Entry(pde).Property(x => x.BankExpenditureNoteDate).IsModified = true;
                        dbContext.Entry(pde).Property(x => x.LastModifiedAgent).IsModified = true;
                        dbContext.Entry(pde).Property(x => x.LastModifiedBy).IsModified = true;
                        dbContext.Entry(pde).Property(x => x.LastModifiedUtc).IsModified = true;
                    }

                    EntityExtension.FlagForDelete(bankExpenditureNote, username, USER_AGENT);
                    dbSet.Update(bankExpenditureNote);
                    Count = await dbContext.SaveChangesAsync();
                    //DeleteDailyBankTransaction(bankExpenditureNote.DocumentNo, identityService);
                    //DeleteCreditorAccount(bankExpenditureNote, identityService);
                    //ReverseJournalTransaction(bankExpenditureNote);
                    transaction.Commit();
                }
                catch (DbUpdateConcurrencyException e)
                {
                    transaction.Rollback();
                    throw new Exception(e.Message);
                }
                catch (Exception e)
                {
                    transaction.Rollback();
                    throw new Exception(e.Message);
                }
            }

            return Count;
        }

        public ReadResponse<object> GetAllByPosition(int Page, int Size, string Order, string Keyword, string Filter)
        {
            var query = dbContext.PurchasingDocumentExpeditions.AsQueryable();


            query = query.Include(i => i.Items);

            if (!string.IsNullOrWhiteSpace(Keyword))
            {
                List<string> searchAttributes = new List<string>()
                {
                    "UnitPaymentOrderNo", "SupplierName", "DivisionName", "SupplierCode", "InvoiceNo"
                };

                query = QueryHelper<PurchasingDocumentExpedition>.ConfigureSearch(query, searchAttributes, Keyword);
            }

            if (Filter.Contains("verificationFilter"))
            {
                Filter = "{}";
                List<ExpeditionPosition> positions = new List<ExpeditionPosition> { ExpeditionPosition.SEND_TO_PURCHASING_DIVISION, ExpeditionPosition.SEND_TO_ACCOUNTING_DIVISION, ExpeditionPosition.SEND_TO_CASHIER_DIVISION };
                query = query.Where(p => positions.Contains(p.Position));
            }

            Dictionary<string, string> FilterDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(Filter);
            query = QueryHelper<PurchasingDocumentExpedition>.ConfigureFilter(query, FilterDictionary);

            Dictionary<string, string> OrderDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(Order);
            query = QueryHelper<PurchasingDocumentExpedition>.ConfigureOrder(query, OrderDictionary);

            Pageable<PurchasingDocumentExpedition> pageable = new Pageable<PurchasingDocumentExpedition>(query, Page - 1, Size);
            List<PurchasingDocumentExpedition> Data = pageable.Data.ToList();
            int TotalData = pageable.TotalCount;

            List<object> list = new List<object>();
            list.AddRange(Data.Select(s => new
            {
                UnitPaymentOrderId = s.Id,
                s.UnitPaymentOrderNo,
                s.UPODate,
                s.DueDate,
                s.InvoiceNo,
                s.SupplierCode,
                s.SupplierName,
                s.CategoryCode,
                s.CategoryName,
                s.DivisionCode,
                s.DivisionName,
                s.Vat,
                s.IncomeTax,
                s.IncomeTaxBy,
                s.IsPaid,
                s.TotalPaid,
                s.Currency,
                s.PaymentMethod,
                s.URNId,
                s.URNNo,
                Items = s.Items.Select(sl => new
                {
                    UnitPaymentOrderItemId = sl.Id,
                    sl.UnitId,
                    sl.UnitCode,
                    sl.UnitName,
                    sl.ProductId,
                    sl.ProductCode,
                    sl.ProductName,
                    sl.Quantity,
                    sl.Uom,
                    sl.Price,
                    sl.URNId,
                    sl.URNNo
                }).ToList()
            }));

            return new ReadResponse<object>(list, TotalData, OrderDictionary);
        }

        public ReadResponse<object> GetReport(int Size, int Page, string DocumentNo, string UnitPaymentOrderNo, string InvoiceNo, string SupplierCode, string DivisionCode, string PaymentMethod, DateTimeOffset? DateFrom, DateTimeOffset? DateTo, int Offset)
        {
            IQueryable<BankExpenditureNoteReportViewModel> Query;
            TimeSpan offset = new TimeSpan(7, 0, 0);

            DateFrom = DateFrom.HasValue ? DateFrom : DateTimeOffset.MinValue;
            DateTo = DateTo.HasValue ? DateTo : DateFrom.GetValueOrDefault().AddMonths(1);

            if (DateFrom == null || DateTo == null)
            {
                Query = (from a in dbContext.BankExpenditureNotes
                         join b in dbContext.BankExpenditureNoteDetails on a.Id equals b.BankExpenditureNoteId
                         join c in dbContext.PurchasingDocumentExpeditions on new { BankExpenditureNoteNo = b.BankExpenditureNote.DocumentNo, b.UnitPaymentOrderNo } equals new { c.BankExpenditureNoteNo, c.UnitPaymentOrderNo }
                         //where c.InvoiceNo == (InvoiceNo ?? c.InvoiceNo)
                         //   && c.SupplierCode == (SupplierCode ?? c.SupplierCode)
                         //   && c.UnitPaymentOrderNo == (UnitPaymentOrderNo ?? c.UnitPaymentOrderNo)
                         //   && c.DivisionCode == (DivisionCode ?? c.DivisionCode)
                         //   && !c.PaymentMethod.ToUpper().Equals("CASH")
                         //   && c.IsPaid
                         //   && c.PaymentMethod == (PaymentMethod ?? c.PaymentMethod)
                         //where a.DocumentNo == (DocumentNo ?? a.DocumentNo)
                         orderby a.DocumentNo
                         select new BankExpenditureNoteReportViewModel
                         {
                             DocumentNo = a.DocumentNo,
                             Currency = a.BankCurrencyCode,
                             Date = a.Date,
                             SupplierCode = c.SupplierCode,
                             SupplierName = c.SupplierName,
                             CategoryName = c.CategoryName == null ? "-" : c.CategoryName,
                             DivisionName = c.DivisionName,
                             PaymentMethod = c.PaymentMethod,
                             UnitPaymentOrderNo = b.UnitPaymentOrderNo,
                             BankName = string.Concat(a.BankAccountName, " - ", a.BankName, " - ", a.BankAccountNumber, " - ", a.BankCurrencyCode),
                             DPP = b.TotalPaid  - b.Vat,
                             VAT = b.Vat,
                             TotalPaid = b.TotalPaid,
                             InvoiceNumber = c.InvoiceNo,
                             DivisionCode = c.DivisionCode
                         }
                      );
            }
            else
            {
                Query = (from a in dbContext.BankExpenditureNotes
                         join b in dbContext.BankExpenditureNoteDetails on a.Id equals b.BankExpenditureNoteId
                         join c in dbContext.PurchasingDocumentExpeditions on new { BankExpenditureNoteNo = b.BankExpenditureNote.DocumentNo, b.UnitPaymentOrderNo } equals new { c.BankExpenditureNoteNo, c.UnitPaymentOrderNo }
                         //where c.InvoiceNo == (InvoiceNo ?? c.InvoiceNo)
                         //   && c.SupplierCode == (SupplierCode ?? c.SupplierCode)
                         //   && c.UnitPaymentOrderNo == (UnitPaymentOrderNo ?? c.UnitPaymentOrderNo)
                         //   && c.DivisionCode == (DivisionCode ?? c.DivisionCode)
                         //   && !c.PaymentMethod.ToUpper().Equals("CASH")
                         //   && c.IsPaid
                         //   && c.PaymentMethod == (PaymentMethod ?? c.PaymentMethod)
                         //where a.DocumentNo == (DocumentNo ?? a.DocumentNo) && a.Date.AddHours(Offset).Date >= DateFrom.Value.Date && a.Date.AddHours(Offset).Date <= DateTo.Value.Date
                         orderby a.DocumentNo
                         select new BankExpenditureNoteReportViewModel
                         {
                             DocumentNo = a.DocumentNo,
                             Currency = a.BankCurrencyCode,
                             Date = a.Date,
                             SupplierCode = c.SupplierCode,
                             SupplierName = c.SupplierName,
                             CategoryName = c.CategoryName == null ? "-" : c.CategoryName,
                             DivisionName = c.DivisionName,
                             PaymentMethod = c.PaymentMethod,
                             UnitPaymentOrderNo = b.UnitPaymentOrderNo,
                             BankName = string.Concat(a.BankAccountName, " - ", a.BankName, " - ", a.BankAccountNumber, " - ", a.BankCurrencyCode),
                             DPP = b.TotalPaid - b.Vat,
                             VAT = b.Vat,
                             TotalPaid = b.TotalPaid,
                             InvoiceNumber = c.InvoiceNo,
                             DivisionCode = c.DivisionCode,
                             TotalDPP = b.TotalPaid - b.Vat,
                             TotalPPN = c.Vat,
                         }
                      );
            }

            Query = Query.Where(entity => entity.Date.ToOffset(offset) >= DateFrom.GetValueOrDefault().ToOffset(offset) && entity.Date.ToOffset(offset) <= DateTo.GetValueOrDefault().ToOffset(offset));
            // override duplicate 
            Query = Query.GroupBy(
                key => new { key.BankName, key.CategoryName, key.Currency,key.Date, key.DivisionCode,key.DivisionName,key.DocumentNo,key.DPP,key.InvoiceNumber,key.PaymentMethod,key.SupplierCode,key.SupplierName,key.TotalDPP,key.TotalPaid,key.TotalPPN,key.VAT,key.UnitPaymentOrderNo},
                value => value,
                (key, value) => new BankExpenditureNoteReportViewModel
                {
                    DocumentNo = key.DocumentNo,
                    Currency = key.Currency,
                    Date = key.Date,
                    SupplierCode = key.SupplierCode,
                    SupplierName = key.SupplierName,
                    CategoryName = key.CategoryName == null ? "-" : key.CategoryName,
                    DivisionName = key.DivisionName,
                    PaymentMethod = key.PaymentMethod,
                    UnitPaymentOrderNo = key.UnitPaymentOrderNo,
                    BankName = key.BankName,
                    DPP = key.DPP,
                    VAT = key.VAT,
                    TotalPaid = key.TotalPaid,
                    InvoiceNumber = key.InvoiceNumber,
                    DivisionCode = key.DivisionCode,
                    TotalDPP = key.TotalDPP,
                    TotalPPN = key.TotalPPN
                }
                );
            if (!string.IsNullOrWhiteSpace(DocumentNo))
                Query = Query.Where(entity => entity.DocumentNo == DocumentNo);

            if (!string.IsNullOrWhiteSpace(UnitPaymentOrderNo))
                Query = Query.Where(entity => entity.UnitPaymentOrderNo == UnitPaymentOrderNo);

            if (!string.IsNullOrWhiteSpace(InvoiceNo))
                Query = Query.Where(entity => entity.InvoiceNumber == InvoiceNo);

            if (!string.IsNullOrWhiteSpace(SupplierCode))
                Query = Query.Where(entity => entity.SupplierCode == SupplierCode);

            if (!string.IsNullOrWhiteSpace(PaymentMethod))
                Query = Query.Where(entity => entity.PaymentMethod == PaymentMethod);

            if (!string.IsNullOrWhiteSpace(DivisionCode))
                Query = Query.Where(entity => entity.DivisionCode == DivisionCode);

            Pageable<BankExpenditureNoteReportViewModel> pageable = new Pageable<BankExpenditureNoteReportViewModel>(Query, Page - 1, Size);
            List<object> data = pageable.Data.ToList<object>();

            return new ReadResponse<object>(data, pageable.TotalCount, new Dictionary<string, string>());
        }

        public void CreateDailyBankTransaction(BankExpenditureNoteModel model, IdentityService identityService)
        {
            DailyBankTransactionViewModel modelToPost = new DailyBankTransactionViewModel()
            {
                Bank = new ViewModels.NewIntegrationViewModel.AccountBankViewModel()
                {
                    Id = model.BankId,
                    Code = model.BankCode,
                    AccountName = model.BankAccountName,
                    AccountNumber = model.BankAccountNumber,
                    BankCode = model.BankCode,
                    BankName = model.BankName,
                    Currency = new ViewModels.NewIntegrationViewModel.CurrencyViewModel()
                    {
                        Code = model.BankCurrencyCode,
                        Id = model.BankCurrencyId,
                    }
                },
                Date = model.Date,
                Nominal = model.GrandTotal,
                CurrencyRate = model.CurrencyRate,
                ReferenceNo = model.DocumentNo,
                ReferenceType = "Bayar Hutang",
                Remark = model.CurrencyCode != "IDR" ? $"Pembayaran atas {model.BankCurrencyCode} dengan nominal {string.Format("{0:n}", model.GrandTotal)} dan kurs {model.CurrencyCode}" : "",
                SourceType = "Operasional",
                Status = "OUT",
                Supplier = new NewSupplierViewModel()
                {
                    _id = model.SupplierId,
                    code = model.SupplierCode,
                    name = model.SupplierName
                },
                IsPosted = true
            };

            if (model.BankCurrencyCode != "IDR")
                modelToPost.NominalValas = model.GrandTotal * model.CurrencyRate;

            string dailyBankTransactionUri = "daily-bank-transactions";
            //var httpClient = new HttpClientService(identityService);
            var httpClient = (IHttpClientService)this.serviceProvider.GetService(typeof(IHttpClientService));
            var response = httpClient.PostAsync($"{APIEndpoint.Finance}{dailyBankTransactionUri}", new StringContent(JsonConvert.SerializeObject(modelToPost).ToString(), Encoding.UTF8, General.JsonMediaType)).Result;
            response.EnsureSuccessStatusCode();
        }

        //public void DeleteDailyBankTransaction(string documentNo, IdentityService identityService)
        //{
        //    string dailyBankTransactionUri = "daily-bank-transactions/by-reference-no/";
        //    //var httpClient = new HttpClientService(identityService);
        //    var httpClient = (IHttpClientService)serviceProvider.GetService(typeof(IHttpClientService));
        //    var response = httpClient.DeleteAsync($"{APIEndpoint.Finance}{dailyBankTransactionUri}{documentNo}").Result;
        //    response.EnsureSuccessStatusCode();
        //}

        private void CreateCreditorAccount(BankExpenditureNoteModel model, IdentityService identityService)
        {
            List<CreditorAccountViewModel> postedData = new List<CreditorAccountViewModel>();
            foreach (var item in model.Details)
            {
                CreditorAccountViewModel viewModel = new CreditorAccountViewModel()
                {
                    Code = model.DocumentNo,
                    Date = model.Date,
                    Id = (int)model.Id,
                    InvoiceNo = item.InvoiceNo,
                    Mutation = item.TotalPaid,
                    SupplierCode = model.SupplierCode,
                    SupplierName = model.SupplierName
                };
                postedData.Add(viewModel);
            }


            var httpClient = (IHttpClientService)this.serviceProvider.GetService(typeof(IHttpClientService));
            var response = httpClient.PostAsync($"{APIEndpoint.Finance}{CREDITOR_ACCOUNT_URI}", new StringContent(JsonConvert.SerializeObject(postedData).ToString(), Encoding.UTF8, General.JsonMediaType)).Result;
            response.EnsureSuccessStatusCode();
        }

        //private void UpdateCreditorAccount(BankExpenditureNoteModel model, IdentityService identityService)
        //{
        //    List<CreditorAccountViewModel> postedData = new List<CreditorAccountViewModel>();
        //    foreach (var item in model.Details)
        //    {
        //        CreditorAccountViewModel viewModel = new CreditorAccountViewModel()
        //        {
        //            Code = model.DocumentNo,
        //            Date = model.Date,
        //            Id = (int)model.Id,
        //            InvoiceNo = item.InvoiceNo,
        //            Mutation = item.TotalPaid,
        //            SupplierCode = model.SupplierCode,
        //            SupplierName = model.SupplierName
        //        };
        //        postedData.Add(viewModel);
        //    }


        //    var httpClient = (IHttpClientService)this.serviceProvider.GetService(typeof(IHttpClientService));
        //    var response = httpClient.PutAsync($"{APIEndpoint.Finance}{CREDITOR_ACCOUNT_URI}", new StringContent(JsonConvert.SerializeObject(postedData).ToString(), Encoding.UTF8, General.JsonMediaType)).Result;
        //    response.EnsureSuccessStatusCode();

        //}

        //private void DeleteCreditorAccount(BankExpenditureNoteModel model, IdentityService identityService)
        //{
        //    var httpClient = (IHttpClientService)this.serviceProvider.GetService(typeof(IHttpClientService));
        //    var response = httpClient.DeleteAsync($"{APIEndpoint.Finance}{CREDITOR_ACCOUNT_URI}/{model.DocumentNo}").Result;
        //    response.EnsureSuccessStatusCode();

        //}

        public List<ExpenditureInfo> GetByPeriod(int month, int year, int timeoffset)
        {
            if (month == 0 && year == 0)
            {
                return dbSet.Select(s => new ExpenditureInfo() { DocumentNo = s.DocumentNo, BankName = s.BankName, BGCheckNumber = s.BGCheckNumber }).ToList();
            }
            else
            {
                return dbSet.Where(w => w.Date.AddHours(timeoffset).Month.Equals(month) && w.Date.AddHours(timeoffset).Year.Equals(year)).Select(s => new ExpenditureInfo() { DocumentNo = s.DocumentNo, BankName = s.BankName, BGCheckNumber = s.BGCheckNumber }).ToList();
            }

        }

        public async Task<int> Posting(List<long> ids)
        {
            var models = dbContext.BankExpenditureNotes.Include(entity => entity.Details).ThenInclude(detail => detail.Items).Where(entity => ids.Contains(entity.Id)).ToList();
            var identityService = serviceProvider.GetService<IdentityService>();
            var upoNos = models.SelectMany(model => model.Details).Select(detail => detail.UnitPaymentOrderNo).ToList();
            var unitPaymentOrders = dbContext
                .UnitPaymentOrders
                .Where(upo => upoNos.Contains(upo.UPONo))
                .ToList()
                .Select(upo =>
                {
                    upo.IsPaid = true;
                    EntityExtension.FlagForUpdate(upo, identityService.Username, USER_AGENT);
                    return upo;
                })
                .ToList();
            dbContext.UnitPaymentOrders.UpdateRange(unitPaymentOrders);

            //var bankExpenditureNoteNos = models.Select(element => element.DocumentNo).ToList();
            //var expeditions = dbContext
            //    .PurchasingDocumentExpeditions
            //    .Where(pde => bankExpenditureNoteNos.Contains(pde.BankExpenditureNoteNo))
            //    .ToList()
            //    .Select(pde =>
            //    {
            //        pde.IsPaid = true;
            //        EntityExtension.FlagForUpdate(pde, identityService.Username, USER_AGENT);
            //        return pde;
            //    })
            //    .ToList();
            //dbContext.PurchasingDocumentExpeditions.UpdateRange(expeditions);

            foreach (var model in models)
            {
                model.IsPosted = true;
                await CreateJournalTransaction(model, identityService);
                CreateDailyBankTransaction(model, identityService);
                CreateCreditorAccount(model, identityService);
                EntityExtension.FlagForUpdate(model, identityService.Username, USER_AGENT);
            }

            dbContext.BankExpenditureNotes.UpdateRange(models);
            return dbContext.SaveChanges();
        }
    }

    public class ExpenditureInfo
    {
        public string DocumentNo { get; set; }
        public string BankName { get; set; }
        public string BGCheckNumber { get; set; }
    }
}
