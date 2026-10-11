using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Client.VSTO.Mvvm;
using SwiftExport.Client.VSTO.Services.Caching;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.Services;
using SwiftExport.Client.VSTO.Features.Sales.Orders;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Models;
using SwiftExport.Contracts.Apis.ForExcel;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Apis.CustomerApis;

namespace SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.ViewModels
{
    /// <summary>采购本地编辑、地址读取、检验及无来源草稿创建；不访问 Excel COM。</summary>
    public sealed partial class PurchaseOrderImportViewModel : ViewModelBaseEx
    {
        private readonly SessionService session;
        private readonly IUiDispatcher dispatcher;
        private readonly ApiCallRunner errors;
        private readonly ISalesOrderImportPrompt prompt;
        private readonly HashSet<PurchaseOrderImportLine> tracked = new HashSet<PurchaseOrderImportLine>();
        private readonly List<PurchaseOrderImportLine> selectedLines = new List<PurchaseOrderImportLine>();
        private PurchaseOrderImportHeader header = new PurchaseOrderImportHeader();
        private SupplierDto selectedSupplier;
        private bool ready, initializing, parsing, resetting, applyingParsedPart, applyingSubmission, dirty;
        private long pageVersion;
        private string parseMessage = "", customerName = "", orderNo = "";
        /// <summary>手工新增后请求视图选中并滚动定位；批量读取不逐行抢占选择。</summary>
        public event Action<PurchaseOrderImportLine> LineAdded;
        /// <summary>构造只接线，基础资料及配置首次使用才加载。</summary>
        public PurchaseOrderImportViewModel(IReferenceDataCacheService referenceData, SessionService session, IUiDispatcher dispatcher,
            ApiCallRunner errors, ISalesOrderImportPrompt prompt, ExcelReadProfileService profiles, IExcelAddressReader reader,
            ExcelImportRegistry registry, IForExcelPurchaseOrderApi checkApi, IProcurementOrderApi orderApi, ICustomerPoConfigApi configApi)
        {
            ReferenceData=referenceData; this.session=session; this.dispatcher=dispatcher; this.errors=errors; this.prompt=prompt;
            this.checkApi=checkApi; this.orderApi=orderApi; this.configApi=configApi; pageVersion=session.Capture().Version; Title="采购订单导入";
            header.PropertyChanged+=HeaderChanged; Lines.CollectionChanged+=LinesChanged; session.Changed+=SessionChanged;
            ((INotifyCollectionChanged)ReferenceData.DictionaryValues).CollectionChanged+=DictionaryChanged;
            InitializeCommand=new AsyncRelayCommand(InitializeAsync,()=>Accept(pageVersion) && !initializing && !parsing && !SubmissionLocked);
            AddStandardLineCommand=new RelayCommand(()=>AddLine(false),()=>IsEditable);
            AddNonStandardLineCommand=new RelayCommand(()=>AddLine(true),()=>IsEditable);
            DeleteSelectedLineCommand=new RelayCommand(DeleteLines,()=>IsEditable && selectedLines.Any(Lines.Contains));
            ToggleNonStandardCommand=new RelayCommand<PurchaseOrderImportLine>(l=>l.SetNonStandard(!l.IsNonStandard),l=>IsEditable && l!=null && Lines.Contains(l));
            ChooseMatchCommand=new RelayCommand<PurchaseOrderImportLine>(l=>l.ChooseMatchCandidate(),l=>IsEditable && l!=null && Lines.Contains(l));
            ClearDeliverySupplierCommand=new RelayCommand<PurchaseOrderImportLine>(l=>l.DeliverySupplierId=null,l=>IsEditable && l!=null && Lines.Contains(l));
            CheckImportCommand=new AsyncRelayCommand(CheckImportAsync,()=>CanMatchSkus && !configLoading && Lines.Count>0);
            MatchAllSkusCommand=new AsyncRelayCommand(()=>MatchSkusAsync(),()=>CanMatchSkus && Lines.Count>0);
            MatchLineSkuCommand=new AsyncRelayCommand<PurchaseOrderImportLine>(l=>MatchSkusAsync(l),l=>CanMatchSkus && l!=null && Lines.Contains(l));
            CustomerSelectedCommand=new RelayCommand(CommitCustomer,()=>IsEditable);
            CustomerClearedCommand=new RelayCommand(()=>CommitCustomer(null),()=>IsEditable);
            RefreshDefaultsCommand=new AsyncRelayCommand(()=>LoadDefaultsAsync(true),()=>IsEditable && !configLoading && HasParties);
            SaveDraftCommand=new AsyncRelayCommand(SaveDraftAsync,()=>CanSaveDraft);
            RetrySaveCommand=new AsyncRelayCommand(SaveDraftAsync,()=>Accept(pageVersion) && HasCreatePermission && HasPendingSave && !saving);
            NewImportCommand=new RelayCommand(StartNewImport,()=>Accept(pageVersion) && completed && !saving);
            foreach(var command in new IRelayCommand[] { InitializeCommand,AddStandardLineCommand,AddNonStandardLineCommand,DeleteSelectedLineCommand,ToggleNonStandardCommand,ChooseMatchCommand,ClearDeliverySupplierCommand,CheckImportCommand,MatchAllSkusCommand,MatchLineSkuCommand,CustomerSelectedCommand,CustomerClearedCommand,RefreshDefaultsCommand,SaveDraftCommand,RetrySaveCommand,NewImportCommand }) RegisterCommand(command);
            InitializeAddresses(profiles,reader,registry);
        }
        /// <summary>借用缓存，不编辑其中 DTO。</summary>
        public IReferenceDataCacheService ReferenceData { get; }
        /// <summary>页面单头，身份字段不参加地址赋值。</summary>
        public PurchaseOrderImportHeader Header => header;
        /// <summary>当前内存明细。</summary>
        public ObservableCollection<PurchaseOrderImportLine> Lines { get; } = new ObservableCollection<PurchaseOrderImportLine>();
        /// <summary>用户明确选择实际采购供应商。</summary>
        public SupplierDto SelectedSupplier { get => selectedSupplier; set { if(SetProperty(ref selectedSupplier,value) && !resetting) { CancelDefaults(); InvalidateSubmission(); dirty=true; NotifyState(); RequestDefaults(); } } }
        /// <summary>缓存明确定位的客户名称，不按名称猜身份。</summary>
        public string CustomerName { get => customerName; private set => SetProperty(ref customerName,value); }
        /// <summary>系统生成的采购号，与 Excel BoNo 分开。</summary>
        public string OrderNo { get => orderNo; private set => SetProperty(ref orderNo,value); }
        /// <summary>非标单位候选，最终由后端校验启用状态。</summary>
        public IReadOnlyList<string> Units => ReferenceData.GetDictionaryValues("Unit").Select(d=>d.DataNameEn).Where(s=>!string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
        /// <summary>认证及加载完成才编辑，检查保存期间锁定。</summary>
        public bool IsEditable => ready && !initializing && !parsing && !SubmissionLocked && Accept(pageVersion);
        /// <summary>DataGrid 只读门禁。</summary>
        public bool IsReadOnly => !IsEditable;
        /// <summary>未保存修改标记。</summary>
        public bool HasEdits => dirty;
        /// <summary>地址读取来源及解析反馈。</summary>
        public string ParseMessage { get => parseMessage; private set => SetProperty(ref parseMessage,value); }
        /// <summary>输入数量统计，不代表履约进度。</summary>
        public decimal? TotalQuantity => Sum(l=>l.Quantity);
        /// <summary>CNY 未税行金额，不应用单头优惠。</summary>
        public decimal? TotalAmount => Sum(l=>l.Amount);
        /// <summary>本地输入及不完整统计反馈。</summary>
        public string ValidationSummary => Header.Error+"\n"+string.Join("\n",Lines.Select((l,i)=>string.IsNullOrEmpty(l.InputError) ? "" : "第 "+(i+1)+" 行："+l.InputError))
            +(Lines.Any(l=>!string.IsNullOrEmpty(l.InputError) || !l.Amount.HasValue) || !TotalAmount.HasValue ? "\n汇总不完整，请修正输入。" : "");
        /// <summary>首次加载及失败重试。</summary>
        public IAsyncRelayCommand InitializeCommand { get; }
        /// <summary>添加标准行。</summary>
        public IRelayCommand AddStandardLineCommand { get; }
        /// <summary>添加非标行。</summary>
        public IRelayCommand AddNonStandardLineCommand { get; }
        /// <summary>多选删除。</summary>
        public IRelayCommand DeleteSelectedLineCommand { get; }
        /// <summary>明确转换行类型。</summary>
        public IRelayCommand<PurchaseOrderImportLine> ToggleNonStandardCommand { get; }
        /// <summary>明确采用歧义候选。</summary>
        public IRelayCommand<PurchaseOrderImportLine> ChooseMatchCommand { get; }
        /// <summary>清除误选的送达供应商，不修改实际采购供应商。</summary>
        public IRelayCommand<PurchaseOrderImportLine> ClearDeliverySupplierCommand { get; }
        /// <summary>首次加载成功后重复 Loaded 不重置编辑，失败允许重试。</summary>
        public async Task InitializeAsync()
        {
            if(!Accept(pageVersion) || initializing || ready || parsing || SubmissionLocked) return;
            var version=pageVersion; initializing=true; NotifyState();
            try { var result=await ReferenceData.EnsureLoadedAsync(); await DeliverAsync(()=> { if(!Accept(version)) return; ready=result.Success; ErrorMessage=result.Success ? "" : result.Message; OnPropertyChanged(nameof(Units)); }); }
            catch(Exception ex) { await DeliverAsync(()=> { if(Accept(version)) ErrorMessage=errors.Describe(ex); }); }
            finally { await DeliverAsync(()=> { if(Accept(version)) { initializing=false; NotifyState(); } }); }
        }
        /// <summary>接纳视图选择快照，不持有控件。</summary>
        public void SetSelectedLines(IEnumerable<PurchaseOrderImportLine> values) { selectedLines.Clear(); selectedLines.AddRange(values.Where(Lines.Contains)); RefreshCommands(); }
        /// <summary>读写期间拒绝普通关闭，未保存和未知结果须确认；宿主退出跳过。</summary>
        public bool CanClose() => !parsing && !checking && !saving && !profilesLoading && (!dirty || prompt.ConfirmDiscard(HasPendingSave
            ? "保存结果未知，关闭会丢失原请求。CommandId："+PendingCommandId+"。是否关闭？" : "内存编辑尚未保存，关闭将丢失。是否关闭？"));
        private bool Accept(long version) => !IsDisposed && session.Capture().User!=null && pageVersion==version && session.Capture().Version==version;
        private bool HasCreatePermission => session.Capture().User?.Permissions?.Contains(PermissionKeys.ProcurementOrderCreate)==true;
        /// <summary>手工新行复制当前交期，集合完成赋号后交给视图定位。</summary>
        private void AddLine(bool nonStandard)
        {
            if(!IsEditable) return;
            var line=new PurchaseOrderImportLine { QuantityText="1", PlannedDeliveryDate=Header.DeliveryDate };
            line.SetNonStandard(nonStandard); Lines.Add(line); LineAdded?.Invoke(line);
        }
        private void DeleteLines() { if(!IsEditable) return; foreach(var line in selectedLines.ToArray()) Lines.Remove(line); selectedLines.Clear(); NotifyState(); }
        // 集合重置也必须解绑旧行；服务器接纳不产生用户编辑。
        private void LinesChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            if(applyingParsedPart) return;
            // 新读取的行按集合顺序赋号；手工新增从当前最大编号继续，不重排行号。
            var lastNumber=Lines.Select(l=>l.LineNumber).DefaultIfEmpty(0).Max();
            foreach(var line in Lines.Where(l=>l.LineNumber==0)) line.AssignLineNumber(++lastNumber);
            foreach(var line in tracked.Where(l=>!Lines.Contains(l)).ToArray()) { line.PropertyChanged-=LineChanged; tracked.Remove(line); }
            foreach(var line in Lines.Where(l=>!tracked.Contains(l))) { tracked.Add(line); line.PropertyChanged+=LineChanged; }
            if(!resetting) { dirty=true; InvalidateSubmission(); } NotifyState();
        }
        // 客户代码变化撤销旧关联，保留商业资料供重新匹配。
        private void HeaderChanged(object sender,PropertyChangedEventArgs args)
        {
            if(resetting || applyingParsedPart || applyingSubmission || args.PropertyName==nameof(Header.Error)) return;
            if(args.PropertyName==nameof(Header.CustomerCode)) ResolveCustomerCode();
            dirty=true; InvalidateSubmission(); NotifyState();
        }
        // 行展示通知可能成组触发；只撤销整单资格，不覆盖输入值。
        private void LineChanged(object sender,PropertyChangedEventArgs args)
        {
            if(resetting || applyingParsedPart || applyingSubmission || args.PropertyName==nameof(PurchaseOrderImportLine.SelectedMatchCandidate) || args.PropertyName==nameof(PurchaseOrderImportLine.LineNumber)) return;
            dirty=true; InvalidateSubmission(); NotifyState();
        }
        private decimal? Sum(Func<PurchaseOrderImportLine,decimal?> selector) { try { return Lines.Sum(l=>selector(l)??0m); } catch(OverflowException) { return null; } }
        private void RefreshOptions() { OnPropertyChanged(nameof(Units)); }
        // 其他窗口刷新字典后只更新候选，不覆盖用户单位输入。
        private async void DictionaryChanged(object sender,NotifyCollectionChangedEventArgs args) { await DeliverAsync(()=>OnPropertyChanged(nameof(Units))); }
        private void NotifyState()
        {
            foreach(var key in new[] { nameof(IsEditable),nameof(IsReadOnly),nameof(HasEdits),nameof(IsDefaultsLoading),nameof(CanSaveDraft),nameof(CanMatchSkus),nameof(HasPendingSave),nameof(PendingCommandId),nameof(TotalAmount),nameof(TotalQuantity),nameof(ValidationSummary),nameof(CanSelectReadProfile) }) OnPropertyChanged(key);
            RefreshCommands();
        }
        // HTTP 管线可能从后台发出身份通知，清空编辑必须回到 Excel STA。
        private async void SessionChanged(object sender,EventArgs args)
        {
            await DeliverAsync(()=> { if(IsDisposed || pageVersion==session.Capture().Version) return; resetting=true;
                try { submissionLifetime.Cancel(); submissionLifetime.Dispose(); submissionLifetime=new System.Threading.CancellationTokenSource(); ResetAddressSession(); pageVersion=session.Capture().Version;
                    ready=initializing=parsing=checking=saving=completed=false; pendingSave=null; unknownSave=false; serverCanSave=false;
                    ClearDocument(); ErrorMessage="账号已变化，请重新加载基础资料与读取配置。"; }
                finally { resetting=false; NotifyState(); } });
        }
        private void StartNewImport() { if(!NewImportCommand.CanExecute(null)) return; resetting=true; try { ClearDocument(); completed=false; serverCanSave=false; } finally { resetting=false; NotifyState(); } }
        // 调用者处于 resetting 状态，重建输入不产生脏标记或沿用旧身份。
        private void ClearDocument() { CancelDefaults(); requestedPairs.Clear(); SelectedCustomer=null; DefaultsMessage=""; Lines.Clear(); selectedLines.Clear(); SelectedSupplier=null; header.PropertyChanged-=HeaderChanged; header=new PurchaseOrderImportHeader(); header.PropertyChanged+=HeaderChanged; customerId=Guid.Empty; CustomerName=OrderNo=ParseMessage=SubmissionMessage=""; dirty=false; checkedRevision=-1; documentRevision++; OnPropertyChanged(nameof(Header)); }
        private async Task DeliverAsync(Action action) { if(IsDisposed) return; try { await dispatcher.InvokeAsync(()=> { if(!IsDisposed) action(); }); } catch(OperationCanceledException) { } }
        /// <summary>取消异步请求及配置身份订阅，解绑行通知，不停止宿主消息循环。</summary>
        protected override void DisposeCore() { LineAdded=null; CancelDefaults(); submissionLifetime.Cancel(); submissionLifetime.Dispose(); DisposeAddresses(); session.Changed-=SessionChanged; ((INotifyCollectionChanged)ReferenceData.DictionaryValues).CollectionChanged-=DictionaryChanged; header.PropertyChanged-=HeaderChanged; Lines.CollectionChanged-=LinesChanged; foreach(var line in tracked) line.PropertyChanged-=LineChanged; tracked.Clear(); }
    }
}
