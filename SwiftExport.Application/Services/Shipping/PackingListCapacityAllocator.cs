using System.Numerics;

namespace SwiftExport.Application.Services.Shipping;

/// <summary>箱单库存与采购容量的纯计算分配；调用者先按产品、单位建立合法候选边，不访问数据库。</summary>
public sealed class PackingListCapacityAllocator
{
    /// <summary>独占的供应容量。库存使用仓库产品键的稳定身份，采购使用采购行身份，不得重复声明。</summary>
    public sealed record Supply(Guid Id, decimal Quantity, bool IsAutoReceipt);

    /// <summary>一项成品或客供件需求；客供件不得连接自动收货容量。</summary>
    public sealed record Demand(Guid Id, decimal Quantity, bool IsComponent);

    /// <summary>已通过产品、单位、来源资格校验的候选边；数字越小越优先。</summary>
    public sealed record Candidate(Guid SupplyId, Guid DemandId, int Priority);

    /// <summary>某供应容量对某需求的实际数量，不代表数据库采购分摊关系。</summary>
    public sealed record Allocation(Guid SupplyId, Guid DemandId, decimal Quantity);

    /// <summary>全单可行时返回分配；不可行时只返回逐需求缺口，禁止执行部分方案。</summary>
    public sealed record Result(IReadOnlyList<Allocation> Allocations, IReadOnlyDictionary<Guid, decimal> Shortfalls)
    {
        /// <summary>是否全部需求均已得到覆盖。</summary>
        public bool Success => Shortfalls.Count == 0;
    }

    /// <summary>使用带反向边的最小费用最大流分配；首先最小化自动入库，再选择较低优先级成本。</summary>
    public Result Allocate(IReadOnlyList<Supply> supplies, IReadOnlyList<Demand> demands,
        IReadOnlyList<Candidate> candidates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supplies);
        ArgumentNullException.ThrowIfNull(demands);
        ArgumentNullException.ThrowIfNull(candidates);
        cancellationToken.ThrowIfCancellationRequested();
        if (supplies.Any(x => x.Id == Guid.Empty) || demands.Any(x => x.Id == Guid.Empty)
            || supplies.Select(x => x.Id).Distinct().Count() != supplies.Count
            || demands.Select(x => x.Id).Distinct().Count() != demands.Count)
            throw new ArgumentException("供应与需求身份必须非空且各自唯一，不能重复计算容量。");
        var supplyRows = supplies.OrderBy(x => x.Id).ToArray();
        var demandRows = demands.OrderBy(x => x.Id).ToArray();
        var supplyIndex = supplyRows.Select((x, i) => (x.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var demandIndex = demandRows.Select((x, i) => (x.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var edges = candidates.OrderBy(x => x.Priority).ThenBy(x => x.SupplyId).ThenBy(x => x.DemandId).ToArray();
        if (edges.GroupBy(x => (x.SupplyId, x.DemandId)).Any(x => x.Count() != 1))
            throw new ArgumentException("重复的候选边必须在事实解析时去重。");
        foreach (var edge in edges)
        {
            if (edge.Priority < 0 || !supplyIndex.ContainsKey(edge.SupplyId) || !demandIndex.ContainsKey(edge.DemandId))
                throw new ArgumentException("候选边引用无效或优先级小于零。");
            if (supplyRows[supplyIndex[edge.SupplyId]].IsAutoReceipt && demandRows[demandIndex[edge.DemandId]].IsComponent)
                throw new ArgumentException("不得使用成品自动收货容量补足客供件。");
        }

        var quantities = demandRows.Select(x => ToUnits(x.Quantity)).ToArray();
        var total = quantities.Aggregate(0L, (a, b) => checked(a + b));
        // 一单位自动入库的代价大于全单所有次级成本，确保数量优先于仓库偏好。
        var receiptCost = (BigInteger)total * (edges.Length == 0 ? 0 : edges.Max(x => x.Priority)) + 1;
        var sink = 1 + supplyRows.Length + demandRows.Length;
        var graph = Enumerable.Range(0, sink + 1).Select(_ => new List<Edge>()).ToArray();
        for (var i = 0; i < supplyRows.Length; i++)
            AddEdge(graph, 0, 1 + i, ToUnits(supplyRows[i].Quantity), supplyRows[i].IsAutoReceipt ? receiptCost : 0);
        var demandEdges = new Edge[demandRows.Length];
        for (var i = 0; i < demandRows.Length; i++)
            demandEdges[i] = AddEdge(graph, 1 + supplyRows.Length + i, sink, quantities[i], 0);
        var mapped = new List<(Candidate Candidate, Edge Edge)>();
        foreach (var candidate in edges)
            mapped.Add((candidate, AddEdge(graph, 1 + supplyIndex[candidate.SupplyId],
                1 + supplyRows.Length + demandIndex[candidate.DemandId], total, candidate.Priority)));

        while (Augment(graph, sink, cancellationToken)) { }
        var missing = demandRows.Select((x, i) => (x.Id, Quantity: demandEdges[i].Remaining / 10000m))
            .Where(x => x.Quantity > 0).ToDictionary(x => x.Id, x => x.Quantity);
        if (missing.Count != 0) return new Result(Array.Empty<Allocation>(), missing);
        var allocations = mapped.Where(x => x.Edge.Original > x.Edge.Remaining)
            .Select(x => new Allocation(x.Candidate.SupplyId, x.Candidate.DemandId,
                (x.Edge.Original - x.Edge.Remaining) / 10000m)).ToArray();
        return new Result(allocations, missing);
    }

    /// <summary>仅接受非负四位数量；不静默舍入来源或容量。</summary>
    private static long ToUnits(decimal quantity)
    {
        if (quantity < 0 || decimal.Round(quantity, 4) != quantity)
            throw new ArgumentException("分配数量必须非负且最多四位小数。");
        return checked((long)(quantity * 10000m));
    }

    /// <summary>正向边记录分配，反向边允许撤销先前选择以避免贪心误阻断。</summary>
    private static Edge AddEdge(List<Edge>[] graph, int from, int to, long capacity, BigInteger cost)
    {
        var forward = new Edge(to, graph[to].Count, capacity, cost);
        var reverse = new Edge(from, graph[from].Count, 0, -cost);
        graph[from].Add(forward);
        graph[to].Add(reverse);
        return forward;
    }

    /// <summary>负成本反向边使用 Bellman-Ford；固定节点及边顺序保证相同事实给出相同结果。</summary>
    private static bool Augment(List<Edge>[] graph, int sink, CancellationToken token)
    {
        var distance = new BigInteger?[graph.Length];
        var previous = new (int Node, int Edge)[graph.Length];
        distance[0] = BigInteger.Zero;
        for (var pass = 0; pass < graph.Length - 1; pass++)
        {
            token.ThrowIfCancellationRequested();
            var changed = false;
            for (var node = 0; node < graph.Length; node++)
            {
                if (distance[node] is not { } current) continue;
                for (var index = 0; index < graph[node].Count; index++)
                {
                    var edge = graph[node][index];
                    if (edge.Remaining == 0) continue;
                    var next = current + edge.Cost;
                    if (distance[edge.To] is { } old && old <= next) continue;
                    distance[edge.To] = next;
                    previous[edge.To] = (node, index);
                    changed = true;
                }
            }
            if (!changed) break;
        }
        if (distance[sink] is null) return false;
        var quantity = long.MaxValue;
        for (var node = sink; node != 0; node = previous[node].Node)
            quantity = Math.Min(quantity, graph[previous[node].Node][previous[node].Edge].Remaining);
        for (var node = sink; node != 0; node = previous[node].Node)
        {
            var edge = graph[previous[node].Node][previous[node].Edge];
            edge.Remaining -= quantity;
            graph[node][edge.ReverseIndex].Remaining += quantity;
        }
        return true;
    }

    /// <summary>残量网络边；仅在一次纯计算调用内存在，不是库存余额。</summary>
    private sealed class Edge(int to, int reverseIndex, long capacity, BigInteger cost)
    {
        public int To { get; } = to;
        public int ReverseIndex { get; } = reverseIndex;
        public long Original { get; } = capacity;
        public long Remaining { get; set; } = capacity;
        public BigInteger Cost { get; } = cost;
    }
}
