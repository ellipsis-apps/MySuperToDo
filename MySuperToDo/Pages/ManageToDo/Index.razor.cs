using System.Security.Claims;
using System.Text.Json;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Radzen;

using MySuperToDo.Application.Interfaces;
using MySuperToDo.Domain.Entities;
using MySuperToDo.Domain.Enums;
using DomainUser = MySuperToDo.Domain.Entities.User;

namespace MySuperToDo.Pages.ManageToDo;

public partial class Index : IAsyncDisposable
{
    private IAsyncDisposable? _listsSubscription;
    private IAsyncDisposable? _allItemsEnforcementSubscription;
    private readonly Dictionary<string, IAsyncDisposable> _listMembershipSubscriptions = new();
    private readonly Dictionary<string, IAsyncDisposable> _listChildrenSubscriptions = new();
    private readonly HashSet<string> _itemSubscriptionsById = [];
    private readonly Dictionary<string, ToDoList> _listsBySoul = new();
    private readonly Dictionary<string, HashSet<string>> _itemIdsByListId = new();
    private readonly Dictionary<string, HashSet<string>> _childListIdsByParentId = new();
    private readonly Dictionary<string, ToDoItem> _itemsById = new();
    private TreeNode? _contextNode;
    private TreeNode? _dragNode;
    private bool _allItemsCompletedCompletesList = true;

    private List<TreeNode> _treeData =
    [
        new TreeNode { Text = "📋 All Lists", Expanded = true }
    ];

    private const string AllItemsListId = "all-items";
    private const string AllItemsListName = "All To Do Items";

    private static string ListItemsPath(string listId) => $"list-items/{listId}";
    private static string ListChildrenPath(string listId) => $"list-children/{listId}";

    protected override async Task OnInitializedAsync()
    {
        await LoadUserSettingsAsync();
        await EnsureAllItemsListExistsAsync();
        _listsSubscription = await GunDb.SubscribeMapAsync("lists", OnListReceivedAsync);

        _allItemsEnforcementSubscription = await GunDb.SubscribeMapAsync("items", OnAnyItemReceivedForEnforcementAsync);
    }

    private async Task LoadUserSettingsAsync()
    {
        _allItemsCompletedCompletesList = true;

        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        var principal = authState.User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var username = principal.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        var user = await GunDb.GetOnceAsync<DomainUser>($"users/{username}");
        if (user is null)
        {
            return;
        }

        var settingsId = string.IsNullOrWhiteSpace(user.UserSettingsId)
            ? $"{user.Id}-settings"
            : user.UserSettingsId;

        var settings = await GunDb.GetOnceAsync<UserSettings>($"user-settings/{settingsId}");
        if (settings is not null)
        {
            _allItemsCompletedCompletesList = settings.AllItemsCompletedCompletesList;
        }
    }

    private async Task EnsureAllItemsListExistsAsync()
    {
        var existing = await GunDb.GetOnceAsync<ToDoList>($"lists/{AllItemsListId}");
        if (existing is not null && !string.IsNullOrWhiteSpace(existing.Name))
        {
            return;
        }

        var list = new ToDoList
        {
            Id = AllItemsListId,
            Name = AllItemsListName,
            Status = ToDoStatus.New,
            StatusDate = DateTime.UtcNow
        };

        await GunDb.PutAsync($"lists/{AllItemsListId}", list);
    }

    private async Task OnAnyItemReceivedForEnforcementAsync(string json, string soul)
    {
        var item = JsonSerializer.Deserialize<ToDoItem>(json);
        var itemId = !string.IsNullOrWhiteSpace(item?.Id) ? item!.Id : soul;

        if (string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        await GunDb.PutAsync($"list-items/{AllItemsListId}/{itemId}", new ListItemLink { ItemId = itemId });
    }

    private async Task OnListReceivedAsync(string json, string soul)
    {
        var list = JsonSerializer.Deserialize<ToDoList>(json);
        if (list is null || string.IsNullOrWhiteSpace(list.Id) || string.IsNullOrWhiteSpace(list.Name))
        {
            return;
        }

        _listsBySoul[soul] = list;

        if (!_listMembershipSubscriptions.ContainsKey(list.Id))
        {
            _itemIdsByListId[list.Id] = [];
            _listMembershipSubscriptions[list.Id] = await GunDb.SubscribeMapAsync(
                ListItemsPath(list.Id),
                (membershipJson, membershipSoul) => OnListMembershipReceivedAsync(list.Id, membershipJson, membershipSoul));
        }

        if (!_listChildrenSubscriptions.ContainsKey(list.Id))
        {
            _childListIdsByParentId[list.Id] = [];
            _listChildrenSubscriptions[list.Id] = await GunDb.SubscribeMapAsync(
                ListChildrenPath(list.Id),
                (childJson, childSoul) => OnListChildReceivedAsync(list.Id, childJson, childSoul));
        }

        if (list.Lists is not null && list.Lists.Count > 0)
        {
            foreach (var child in list.Lists.Where(c => !string.IsNullOrWhiteSpace(c.Id)))
            {
                _childListIdsByParentId[list.Id].Add(child.Id);
                await GunDb.PutAsync($"{ListChildrenPath(list.Id)}/{child.Id}", new ListChildLink { ChildListId = child.Id });
            }
        }

        RebuildTree();
        await InvokeAsync(StateHasChanged);
    }

    private Task OnListChildReceivedAsync(string parentListId, string json, string soul)
    {
        var link = JsonSerializer.Deserialize<ListChildLink>(json);
        var childListId = !string.IsNullOrWhiteSpace(link?.ChildListId) ? link!.ChildListId : soul;

        if (string.IsNullOrWhiteSpace(childListId))
        {
            return Task.CompletedTask;
        }

        if (!_childListIdsByParentId.TryGetValue(parentListId, out var childIds))
        {
            childIds = [];
            _childListIdsByParentId[parentListId] = childIds;
        }

        childIds.Add(childListId);

        RebuildTree();
        _ = InvokeAsync(StateHasChanged);
        return Task.CompletedTask;
    }

    private void OnDragStart(TreeNode node)
    {
        _dragNode = node;
    }

    private static void OnDragOver(DragEventArgs _) { }

    private async Task OnDropAsync(TreeNode target)
    {
        if (_dragNode is null)
        {
            return;
        }

        var source = _dragNode;
        _dragNode = null;

        if (ReferenceEquals(source, target) || target.SuppressDragIcon)
        {
            return;
        }

        if (source.IsTodoItem)
        {
            await MoveTodoItemAsync(source, target);
        }
        else
        {
            await MoveListAsync(source, target);
        }

        RebuildTree();
        await InvokeAsync(StateHasChanged);
    }

    private async Task MoveTodoItemAsync(TreeNode source, TreeNode target)
    {
        if (string.IsNullOrWhiteSpace(source.Id) || string.IsNullOrWhiteSpace(source.ListContextId))
        {
            return;
        }

        var targetListId = target.IsTodoItem ? target.ListContextId : target.Id;
        if (string.IsNullOrWhiteSpace(targetListId) || string.Equals(source.ListContextId, targetListId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await GunDb.RemoveAsync($"list-items/{source.ListContextId}/{source.Id}");
        await GunDb.PutAsync($"list-items/{targetListId}/{source.Id}", new ListItemLink { ItemId = source.Id });

        if (_itemIdsByListId.TryGetValue(source.ListContextId, out var fromSet))
        {
            fromSet.Remove(source.Id);
        }

        if (!_itemIdsByListId.TryGetValue(targetListId, out var toSet))
        {
            toSet = [];
            _itemIdsByListId[targetListId] = toSet;
        }
        toSet.Add(source.Id);
    }

    private async Task MoveListAsync(TreeNode source, TreeNode target)
    {
        if (string.IsNullOrWhiteSpace(source.Id))
        {
            return;
        }

        var targetParentId = target.IsTodoItem ? target.ListContextId : target.Id;
        if (string.IsNullOrWhiteSpace(targetParentId))
        {
            return;
        }

        if (string.Equals(source.Id, targetParentId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(source.Id, AllItemsListId, StringComparison.OrdinalIgnoreCase)
            || IsDescendantList(source.Id, targetParentId))
        {
            return;
        }

        foreach (var parentId in _childListIdsByParentId.Keys.ToList())
        {
            if (!_childListIdsByParentId[parentId].Contains(source.Id))
            {
                continue;
            }

            _childListIdsByParentId[parentId].Remove(source.Id);
            await GunDb.RemoveAsync($"{ListChildrenPath(parentId)}/{source.Id}");
        }

        if (!_childListIdsByParentId.TryGetValue(targetParentId, out var targetChildren))
        {
            targetChildren = [];
            _childListIdsByParentId[targetParentId] = targetChildren;
        }

        targetChildren.Add(source.Id);
        await GunDb.PutAsync($"{ListChildrenPath(targetParentId)}/{source.Id}", new ListChildLink { ChildListId = source.Id });
    }

    private bool IsDescendantList(string sourceListId, string potentialDescendantId)
    {
        if (!_childListIdsByParentId.TryGetValue(sourceListId, out var children))
        {
            return false;
        }

        return ContainsListId(children, potentialDescendantId);
    }

    private bool ContainsListId(IEnumerable<string> childIds, string id)
    {
        foreach (var childId in childIds)
        {
            if (string.Equals(childId, id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (_childListIdsByParentId.TryGetValue(childId, out var nested) && ContainsListId(nested, id))
            {
                return true;
            }
        }

        return false;
    }

    private async Task OnListMembershipReceivedAsync(string listId, string json, string soul)
    {
        var link = JsonSerializer.Deserialize<ListItemLink>(json);
        var itemId = !string.IsNullOrWhiteSpace(link?.ItemId) ? link!.ItemId : soul;

        if (string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        if (!_itemIdsByListId.TryGetValue(listId, out var itemIds))
        {
            itemIds = [];
            _itemIdsByListId[listId] = itemIds;
        }

        itemIds.Add(itemId);

        if (!_itemSubscriptionsById.Contains(itemId))
        {
            await GunDb.SubscribeAsync($"items/{itemId}", (itemJson, _) => OnItemReceivedAsync(itemId, itemJson));
            _itemSubscriptionsById.Add(itemId);
        }

        RebuildTree();
        await InvokeAsync(StateHasChanged);
    }

    private Task OnItemReceivedAsync(string itemId, string json)
    {
        var item = JsonSerializer.Deserialize<ToDoItem>(json);
        if (item is null || string.IsNullOrWhiteSpace(item.Title))
        {
            return Task.CompletedTask;
        }

        item.Id = string.IsNullOrWhiteSpace(item.Id) ? itemId : item.Id;
        _itemsById[itemId] = item;

        RebuildTree();
        _ = InvokeAsync(StateHasChanged);
        return Task.CompletedTask;
    }

    private void RebuildTree()
    {
        var root = new TreeNode { Text = "📋 All Lists", Expanded = true, SuppressDragIcon = true };

        var nestedListIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var childIds in _childListIdsByParentId.Values)
        {
            foreach (var childId in childIds)
            {
                nestedListIds.Add(childId);
            }
        }

        foreach (var list in _listsBySoul.Values.GroupBy(l => l.Id).Select(g => g.First()).OrderBy(l => l.Name))
        {
            if (nestedListIds.Contains(list.Id))
            {
                continue;
            }

            root.Children.Add(BuildListNode(list, inAllItemsBranch: false));
        }

        _treeData = [root];
    }

    private TreeNode BuildListNode(ToDoList list, bool inAllItemsBranch)
    {
        var suppressDragIcon = inAllItemsBranch || string.Equals(list.Id, AllItemsListId, StringComparison.OrdinalIgnoreCase);

        var node = new TreeNode
        {
            Id = list.Id,
            ListContextId = list.Id,
            Text = $"📋 {list.Name}",
            Expanded = false,
            SuppressDragIcon = suppressDragIcon,
            IsCompleted = list.Status == ToDoStatus.Completed,
            IsUrgent = list.IsUrgent,
            DueDate = list.DueDate
        };

        if (_childListIdsByParentId.TryGetValue(list.Id, out var childListIds))
        {
            foreach (var childListId in childListIds)
            {
                var childList = _listsBySoul.Values.FirstOrDefault(l => l.Id == childListId);
                if (childList is not null)
                {
                    node.Children.Add(BuildListNode(childList, suppressDragIcon));
                }
            }
        }

        if (_itemIdsByListId.TryGetValue(list.Id, out var itemIds))
        {
            foreach (var itemId in itemIds.OrderBy(i => i))
            {
                if (!_itemsById.TryGetValue(itemId, out var item))
                {
                    continue;
                }

                node.Children.Add(new TreeNode
                {
                    Id = itemId,
                    ListContextId = list.Id,
                    Text = item.Title,
                    IsTodoItem = true,
                    IsCompleted = item.Status == ToDoStatus.Completed,
                    SuppressDragIcon = suppressDragIcon,
                    IsUrgent = item.IsUrgent,
                    DueDate = item.DueDate
                });
            }
        }

        return node;
    }

    private async Task OnTodoItemCheckedChangedAsync(TreeNode node, bool isChecked)
    {
        if (!node.IsTodoItem || string.IsNullOrWhiteSpace(node.Id))
        {
            return;
        }

        var targetStatus = isChecked ? ToDoStatus.Completed : ToDoStatus.New;

        if (!_itemsById.TryGetValue(node.Id, out var item))
        {
            return;
        }

        item.Status = targetStatus;
        await GunDb.PutAsync($"items/{item.Id}", item);

        if (isChecked && !string.IsNullOrWhiteSpace(node.ListContextId))
        {
            await TryAutoCompleteAncestorListsAsync(node.ListContextId);
        }

        node.IsCompleted = isChecked;
        RebuildTree();
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnListCheckedChangedAsync(TreeNode node, bool isChecked)
    {
        if (node.IsTodoItem || string.IsNullOrWhiteSpace(node.Id))
        {
            return;
        }

        var list = _listsBySoul.Values.FirstOrDefault(l => l.Id == node.Id);
        if (list is null)
        {
            return;
        }

        list.Status = isChecked ? ToDoStatus.Completed : ToDoStatus.New;
        await GunDb.PutAsync($"lists/{list.Id}", list);

        if (isChecked)
        {
            await MarkDescendantTreeCompletedAsync(list.Id);
            await TryAutoCompleteAncestorListsAsync(list.Id);
        }

        node.IsCompleted = isChecked;
        RebuildTree();
        await InvokeAsync(StateHasChanged);
    }

    private async Task MarkDescendantTreeCompletedAsync(string parentListId)
    {
        if (_itemIdsByListId.TryGetValue(parentListId, out var childItemIds))
        {
            foreach (var childItemId in childItemIds)
            {
                if (!_itemsById.TryGetValue(childItemId, out var item) || item.Status == ToDoStatus.Completed)
                {
                    continue;
                }

                item.Status = ToDoStatus.Completed;
                await GunDb.PutAsync($"items/{item.Id}", item);
            }
        }

        if (_childListIdsByParentId.TryGetValue(parentListId, out var childListIds))
        {
            foreach (var childListId in childListIds)
            {
                var childList = _listsBySoul.Values.FirstOrDefault(l => l.Id == childListId);
                if (childList is null)
                {
                    continue;
                }

                if (childList.Status != ToDoStatus.Completed)
                {
                    childList.Status = ToDoStatus.Completed;
                    await GunDb.PutAsync($"lists/{childList.Id}", childList);
                }

                await MarkDescendantTreeCompletedAsync(childListId);
            }
        }
    }

    private async Task TryAutoCompleteAncestorListsAsync(string startingListId)
    {
        if (!_allItemsCompletedCompletesList)
        {
            return;
        }

        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue(startingListId);

        while (queue.Count > 0)
        {
            var childId = queue.Dequeue();

            var parentIds = _childListIdsByParentId
                .Where(kvp => kvp.Value.Contains(childId))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var parentId in parentIds)
            {
                if (!visited.Add(parentId))
                {
                    continue;
                }

                var hasTodoChildren = _itemIdsByListId.TryGetValue(parentId, out var itemIds) && itemIds.Count > 0;
                var allTodoChildrenCompleted = !hasTodoChildren || itemIds!.All(id =>
                    _itemsById.TryGetValue(id, out var item) && item.Status == ToDoStatus.Completed);

                var hasListChildren = _childListIdsByParentId.TryGetValue(parentId, out var listIds) && listIds.Count > 0;
                var allListChildrenCompleted = !hasListChildren || listIds!.All(id =>
                    _listsBySoul.Values.FirstOrDefault(l => l.Id == id)?.Status == ToDoStatus.Completed);

                if (!(hasTodoChildren || hasListChildren) || !allTodoChildrenCompleted || !allListChildrenCompleted)
                {
                    continue;
                }

                var parent = _listsBySoul.Values.FirstOrDefault(l => l.Id == parentId);
                if (parent is null)
                {
                    continue;
                }

                if (parent.Status != ToDoStatus.Completed)
                {
                    parent.Status = ToDoStatus.Completed;
                    await GunDb.PutAsync($"lists/{parent.Id}", parent);
                }

                queue.Enqueue(parentId);
            }
        }
    }

    private async Task OnNodeDoubleClickAsync(TreeNode node)
    {
        if (!node.IsTodoItem && string.Equals(node.Id, AllItemsListId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (node.IsTodoItem)
        {
            await OpenTodoItemDetailAsync(node);
            return;
        }

        if (!string.IsNullOrWhiteSpace(node.Id))
        {
            await OpenListDetailAsync(node);
        }
    }

    private void OnTreeItemContextMenu(TreeItemContextMenuEventArgs args)
    {
        _contextNode = args.Value as TreeNode;
        if (_contextNode is null)
        {
            return;
        }

        var isTodo = _contextNode.IsTodoItem;
        var isList = !_contextNode.IsTodoItem && !string.IsNullOrWhiteSpace(_contextNode.Id);
        var isAllItemsList = isList && string.Equals(_contextNode.Id, "all-items", StringComparison.OrdinalIgnoreCase);

        List<ContextMenuItem> menuItems = isAllItemsList
            ?
            [
                new ContextMenuItem { Text = "Add/Edit a To Do item", Value = "todo", Icon = "edit" }
            ]
            :
            [
                new ContextMenuItem { Text = "Add/Edit a List", Value = "list", Icon = "list" },
                new ContextMenuItem { Text = "Add/Edit a To Do item", Value = "todo", Icon = "edit" },
                new ContextMenuItem { Text = "Delete To Do Item", Value = "delete-todo", Icon = "delete", Disabled = !isTodo },
                new ContextMenuItem { Text = "Delete List", Value = "delete-list", Icon = "delete", Disabled = !isList }
            ];

        ContextMenuService.Open(args, menuItems, OnContextMenuItemClick);
    }

    private async void OnContextMenuItemClick(MenuItemEventArgs args)
    {
        if (_contextNode is null || args?.Value is null)
        {
            return;
        }

        switch (args.Value?.ToString())
        {
            case "list":
                await OpenListDetailAsync(_contextNode);
                break;
            case "todo":
                await OpenTodoItemDetailAsync(_contextNode);
                break;
            case "delete-todo":
                await DeleteTodoItemAsync(_contextNode);
                break;
            case "delete-list":
                await DeleteListAsync(_contextNode);
                break;
        }
    }

    private async Task DeleteTodoItemAsync(TreeNode node)
    {
        if (!node.IsTodoItem || string.IsNullOrWhiteSpace(node.Id))
        {
            return;
        }

        var confirmed = await DialogService.Confirm(
            $"Delete To Do Item \"{node.Text}\"?",
            "Delete To Do Item",
            new ConfirmOptions { OkButtonText = "Delete", CancelButtonText = "Cancel" });

        if (confirmed != true)
        {
            return;
        }

        var itemId = node.Id;

        await GunDb.RemoveAsync($"items/{itemId}");

        var listIds = _itemIdsByListId
            .Where(kvp => kvp.Value.Contains(itemId))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var listId in listIds)
        {
            await GunDb.RemoveAsync($"list-items/{listId}/{itemId}");
            _itemIdsByListId[listId].Remove(itemId);
        }

        _itemsById.Remove(itemId);

        if (_itemSubscriptionsById.Contains(itemId))
        {
            await GunDb.UnsubscribeAsync($"items/{itemId}");
            _itemSubscriptionsById.Remove(itemId);
        }

        RebuildTree();
        await InvokeAsync(StateHasChanged);
    }

    private async Task DeleteListAsync(TreeNode node)
    {
        if (node.IsTodoItem || string.IsNullOrWhiteSpace(node.Id))
        {
            return;
        }

        var listId = node.Id;

        if (string.Equals(listId, "all-items", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var listName = _listsBySoul.Values.FirstOrDefault(l => l.Id == listId)?.Name ?? node.Text;

        var confirmed = await DialogService.Confirm(
            $"Delete list \"{listName}\"?",
            "Delete List",
            new ConfirmOptions { OkButtonText = "Delete", CancelButtonText = "Cancel" });

        if (confirmed != true)
        {
            return;
        }

        if (_listMembershipSubscriptions.Remove(listId, out var subscription))
        {
            await subscription.DisposeAsync();
        }

        var removedItemIds = _itemIdsByListId.TryGetValue(listId, out var ids) ? ids.ToList() : [];

        _itemIdsByListId.Remove(listId);

        await GunDb.RemoveAsync($"list-items/{listId}");
        await GunDb.RemoveAsync($"lists/{listId}");

        foreach (var list in _listsBySoul.Values)
        {
            list.Lists?.RemoveAll(l => l.Id == listId);
        }

        var soulsToRemove = _listsBySoul.Where(kvp => kvp.Value.Id == listId).Select(kvp => kvp.Key).ToList();
        foreach (var soul in soulsToRemove)
        {
            _listsBySoul.Remove(soul);
        }

        foreach (var itemId in removedItemIds)
        {
            var stillReferenced = _itemIdsByListId.Values.Any(set => set.Contains(itemId));
            if (stillReferenced)
            {
                continue;
            }

            _itemsById.Remove(itemId);

            if (_itemSubscriptionsById.Contains(itemId))
            {
                await GunDb.UnsubscribeAsync($"items/{itemId}");
                _itemSubscriptionsById.Remove(itemId);
            }
        }

        RebuildTree();
        await InvokeAsync(StateHasChanged);
    }

    private async Task OpenListDetailAsync(TreeNode node)
    {
        Dictionary<string, object>? parameters = null;

        if (!node.IsTodoItem && !string.IsNullOrWhiteSpace(node.Id))
        {
            var existing = _listsBySoul.Values.FirstOrDefault(l => l.Id == node.Id);
            if (existing is not null)
            {
                parameters = new Dictionary<string, object>
                {
                    { "ExistingList", existing }
                };
            }
        }

        await DialogService.OpenAsync<ToDoListDetail>(
            "To Do List Detail",
            parameters,
            new DialogOptions { Width = "420px", ShowClose = true, CloseDialogOnOverlayClick = false });
    }

    private async Task OpenTodoItemDetailAsync(TreeNode node)
    {
        var allItemsListId = _listsBySoul.Values
            .FirstOrDefault(l => string.Equals(l.Name, "All To Do Items", StringComparison.OrdinalIgnoreCase))?.Id
            ?? "all-items";

        var listId = node.ListContextId ?? allItemsListId;

        var parameters = new Dictionary<string, object>
        {
            { "ListId", listId },
            { "AllItemsListId", allItemsListId }
        };

        if (node.IsTodoItem && !string.IsNullOrWhiteSpace(node.Id) && _itemsById.TryGetValue(node.Id, out var item))
        {
            parameters["ExistingItem"] = item;
        }

        await DialogService.OpenAsync<ToDoItemDetail>(
            "To Do Item Detail",
            parameters,
            new DialogOptions { Width = "480px", ShowClose = true, CloseDialogOnOverlayClick = false });
    }

    public async ValueTask DisposeAsync()
    {
        if (_listsSubscription is not null)
        {
            await _listsSubscription.DisposeAsync();
        }

        if (_allItemsEnforcementSubscription is not null)
        {
            await _allItemsEnforcementSubscription.DisposeAsync();
        }

        foreach (var subscription in _listMembershipSubscriptions.Values)
        {
            await subscription.DisposeAsync();
        }

        foreach (var subscription in _listChildrenSubscriptions.Values)
        {
            await subscription.DisposeAsync();
        }

        foreach (var itemId in _itemSubscriptionsById)
        {
            await GunDb.UnsubscribeAsync($"items/{itemId}");
        }

        _listMembershipSubscriptions.Clear();
        _listChildrenSubscriptions.Clear();
        _itemSubscriptionsById.Clear();
    }

    private sealed class TreeNode
    {
        public string Text { get; set; } = string.Empty;
        public string? Id { get; set; }
        public string? ListContextId { get; set; }
        public bool IsTodoItem { get; set; }
        public bool IsCompleted { get; set; }
        public bool Expanded { get; set; }
        public bool SuppressDragIcon { get; set; }
        public bool IsUrgent { get; set; }
        public DateTime? DueDate { get; set; }
        public List<TreeNode> Children { get; set; } = [];
    }

    private sealed class ListItemLink
    {
        public string ItemId { get; set; } = string.Empty;
    }

    private sealed class ListChildLink
    {
        public string ChildListId { get; set; } = string.Empty;
    }
}
