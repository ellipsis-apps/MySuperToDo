using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Radzen;
using MySuperToDo.Application.Interfaces;
using MySuperToDo.Domain.Entities;
using MySuperToDo.Domain.Enums;
using DomainUser = MySuperToDo.Domain.Entities.User;
namespace MySuperToDo.Pages.ManageToDo;
/// <summary>
/// Represents the lists page component for managing ToDo lists.
/// This component displays a hierarchical tree of ToDo lists (excluding "All To Do Items"), supports drag-and-drop for reorganization,
/// real-time updates via GunDB subscriptions, and provides context menus for editing and deleting.
/// It handles user settings, ensures the "All To Do Items" list exists, and manages completion states.
/// </summary>
public partial class Lists : IAsyncDisposable
{
    [Inject] public IJSRuntime JSRuntime { get; set; } = default!;
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

    /// <summary>
    /// Called when the component is initialized asynchronously.
    /// Loads user settings, ensures the "All To Do Items" list exists, and subscribes to GunDB for lists and item enforcement.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task OnInitializedAsync()
    {
        await LoadUserSettingsAsync();
        await EnsureAllItemsListExistsAsync();
        _listsSubscription = await GunDb.SubscribeMapAsync("lists", OnListReceivedAsync);
        _allItemsEnforcementSubscription = await GunDb.SubscribeMapAsync("items", OnAnyItemReceivedForEnforcementAsync);
    }

    /// <summary>
    /// Loads user settings asynchronously from GunDB.
    /// Retrieves the authenticated user's settings, including the flag for auto-completing lists when all items are completed.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Ensures that the "All To Do Items" list exists in GunDB asynchronously.
    /// Checks if the list already exists; if not, creates it with default properties.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Handles the receipt of any item update from GunDB for enforcement asynchronously.
    /// Automatically adds the item to the "All To Do Items" list to ensure all items are tracked.
    /// </summary>
    /// <param name="json">The JSON string representing the ToDo item.</param>
    /// <param name="soul">The soul (key) of the item in GunDB.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Handles the receipt of a list update from GunDB asynchronously.
    /// Updates internal data structures, subscribes to list memberships and children, and rebuilds the tree.
    /// </summary>
    /// <param name="json">The JSON string representing the ToDo list.</param>
    /// <param name="soul">The soul (key) of the list in GunDB.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Handles the receipt of a list child update from GunDB asynchronously.
    /// Updates the internal child list IDs and rebuilds the tree.
    /// </summary>
    /// <param name="parentListId">The ID of the parent list.</param>
    /// <param name="json">The JSON string representing the list child link.</param>
    /// <param name="soul">The soul (key) of the child link in GunDB.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Handles the start of a drag operation on a tree node.
    /// Sets the dragged node for later use in drop operations.
    /// </summary>
    /// <param name="node">The tree node being dragged.</param>
    private void OnDragStart(TreeNode node)
    {
        _dragNode = node;
    }

    /// <summary>
    /// Handles the drag over event for tree nodes.
    /// Currently does nothing, as drag over is not customized.
    /// </summary>
    /// <param name="_">The drag event arguments (unused).</param>
    private static void OnDragOver(DragEventArgs _) { }

    /// <summary>
    /// Handles the drop operation asynchronously after a drag.
    /// Determiners if the dropped item is a ToDo item or list and moves it accordingly.
    /// </summary>
    /// <param name="target">The tree node where the item was dropped.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnDropAsync(TreeNode target)
    {
        if (_dragNode is null)
        {
            return;
        }
        var source = _dragNode;
        _dragNode = null;
        if (ReferenceEquals(source, target) || (target.SuppressDragIcon && !string.IsNullOrWhiteSpace(target.Id)))
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

    /// <summary>
    /// Moves a ToDo item to a different list asynchronously.
    /// Removes the item from the source list and adds it to the target list in GunDB.
    /// </summary>
    /// <param name="source">The source tree node representing the item.</param>
    /// <param name="target">The target tree node representing the list.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Moves a ToDo list to a different parent list asynchronously.
    /// Updates the parent-child relationships in GunDB, avoiding circular references.
    /// </summary>
    /// <param name="source">The source tree node representing the list to move.</param>
    /// <param name="target">The target tree node representing the new parent list.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task MoveListAsync(TreeNode source, TreeNode target)
    {
        if (string.IsNullOrWhiteSpace(source.Id))
        {
            return;
        }
        var targetParentId = target.IsTodoItem ? target.ListContextId : target.Id;
        var isRootDrop = string.IsNullOrWhiteSpace(targetParentId);
        if (!isRootDrop)
        {
            if (string.Equals(source.Id, targetParentId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(source.Id, AllItemsListId, StringComparison.OrdinalIgnoreCase)
                || IsDescendantList(source.Id, targetParentId))
            {
                return;
            }
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
        if (!isRootDrop)
        {
            if (!_childListIdsByParentId.TryGetValue(targetParentId!, out var targetChildren))
            {
                targetChildren = [];
                _childListIdsByParentId[targetParentId!] = targetChildren;
            }
            targetChildren.Add(source.Id);
            await GunDb.PutAsync($"{ListChildrenPath(targetParentId!)}/{source.Id}", new ListChildLink { ChildListId = source.Id });
        }
    }

    /// <summary>
    /// Checks if a list is a descendant of another list.
    /// Recursively checks the child lists to prevent circular references.
    /// </summary>
    /// <param name="sourceListId">The ID of the potential ancestor list.</param>
    /// <param name="potentialDescendantId">The ID of the potential descendant list.</param>
    /// <returns>True if the second list is a descendant of the first, false otherwise.</returns>
    private bool IsDescendantList(string sourceListId, string potentialDescendantId)
    {
        if (!_childListIdsByParentId.TryGetValue(sourceListId, out var children))
        {
            return false;
        }
        return ContainsListId(children, potentialDescendantId);
    }

    /// <summary>
    /// Recursively checks if a list ID is contained within a set of child list IDs.
    /// </summary>
    /// <param name="childIds">The collection of child list IDs to search.</param>
    /// <param name="id">The list ID to find.</param>
    /// <returns>True if the ID is found, false otherwise.</returns>
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

    /// <summary>
    /// Handles the receipt of a list membership update from GunDB asynchronously.
    /// Updates the internal item IDs for the list and subscribes to item updates if necessary.
    /// </summary>
    /// <param name="listId">The ID of the list.</param>
    /// <param name="json">The JSON string representing the list item link.</param>
    /// <param name="soul">The soul (key) of the membership in GunDB.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Handles the receipt of an item update from GunDB asynchronously.
    /// Updates the internal items dictionary and rebuilds the tree.
    /// </summary>
    /// <param name="itemId">The ID of the item.</param>
    /// <param name="json">The JSON string representing the ToDo item.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Rebuilds the tree data structure for display.
    /// Creates a root node and populates it with lists and their children/items.
    /// Excludes the "All To Do Items" list from the top level.
    /// </summary>
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
        foreach (var list in _listsBySoul.Values.GroupBy(l => l.Id).Select(g => g.First()).Where(l => !string.Equals(l.Id, AllItemsListId, StringComparison.OrdinalIgnoreCase)).OrderBy(l => l.Name))
        {
            if (nestedListIds.Contains(list.Id))
            {
                continue;
            }
            root.Children.Add(BuildListNode(list, inAllItemsBranch: false));
        }
        _treeData = [root];
    }

    /// <summary>
    /// Builds a tree node for a ToDo list, including its child lists and items.
    /// </summary>
    /// <param name="list">The ToDo list to build the node for.</param>
    /// <param name="inAllItemsBranch">True if the list is in the "All Items" branch, affecting drag behavior.</param>
    /// <returns>The constructed tree node.</returns>
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
            DueDate = list.DueDate,
            Priority = list.Priority
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
                    Priority = item.Priority,
                    DueDate = item.DueDate
                });
            }
        }
        return node;
    }

    /// <summary>
    /// Handles the checked state change of a ToDo item asynchronously.
    /// Updates the item's status in GunDB and potentially auto-completes ancestor lists.
    /// </summary>
    /// <param name="node">The tree node representing the item.</param>
    /// <param name="isChecked">True if the item is checked (completed), false otherwise.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Handles the checked state change of a ToDo list asynchronously.
    /// Updates the list's status in GunDB and marks all descendants as completed.
    /// </summary>
    /// <param name="node">The tree node representing the list.</param>
    /// <param name="isChecked">True if the list is checked (completed), false otherwise.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Marks all descendants (items and lists) of a parent list as completed asynchronously.
    /// Recursively updates the status in GunDB.
    /// </summary>
    /// <param name="parentListId">The ID of the parent list.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Attempts to auto-complete ancestor lists if all their children are completed asynchronously.
    /// Traverses up the hierarchy and marks lists as completed if all items and child lists are done.
    /// </summary>
    /// <param name="startingListId">The ID of the list to start checking from.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Handles double-click on a tree node asynchronously.
    /// Opens the detail dialog for the item or list, except for the "All Items" list.
    /// </summary>
    /// <param name="node">The tree node that was double-clicked.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Handles the context menu event for tree items.
    /// Sets the context node and opens a context menu with appropriate options based on the node type.
    /// </summary>
    /// <param name="args">The context menu event arguments.</param>
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

    /// <summary>
    /// Handles clicks on context menu items asynchronously.
    /// Performs actions like opening detail dialogs or deleting items/lists based on the selected menu item.
    /// </summary>
    /// <param name="args">The menu item event arguments.</param>
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

    /// <summary>
    /// Deletes a ToDo item after user confirmation asynchronously.
    /// Removes the item from GunDB, all associated lists, and unsubscribes from updates.
    /// </summary>
    /// <param name="node">The tree node representing the item to delete.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Deletes a ToDo list after user confirmation asynchronously.
    /// Removes the list from GunDB, disposes subscriptions, and cleans up orphaned items.
    /// </summary>
    /// <param name="node">The tree node representing the list to delete.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Deletes the entire GunDB database asynchronously.
    /// Clears all data stored in IndexedDB and reloads the page to reinitialize the app.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task DeleteGunDBDatabaseAsync()
    {
        var confirmed = await DialogService.Confirm(
            "This will permanently delete all ToDo data. Are you sure?",
            "Delete All Data",
            new ConfirmOptions { OkButtonText = "Delete", CancelButtonText = "Cancel" });

        if (confirmed != true)
        {
            return;
        }

        try
        {
            await JSRuntime.InvokeVoidAsync("eval", @"
                new Promise((resolve, reject) => {
                    const deleteRequest = indexedDB.deleteDatabase('gun');
                    deleteRequest.onsuccess = () => resolve();
                    deleteRequest.onerror = () => reject(deleteRequest.error);
                    deleteRequest.onblocked = () => reject('Database deletion blocked');
                }).then(() => location.reload()).catch(err => console.error('Failed to delete GunDB database:', err));
            ");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to delete GunDB database: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens the detail dialog for a ToDo list asynchronously.
    /// Passes the existing list data if available.
    /// </summary>
    /// <param name="node">The tree node representing the list.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Opens the detail dialog for a ToDo item asynchronously.
    /// Passes the list context and existing item data if available.
    /// </summary>
    /// <param name="node">The tree node representing the item.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Disposes of the component's resources asynchronously.
    /// Disposes all GunDB subscriptions.
    /// </summary>
    /// <returns>A ValueTask representing the asynchronous disposal.</returns>
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

    /// <summary>
    /// Represents a node in the tree structure for displaying lists and items.
    /// Contains properties for display, hierarchy, and behavior.
    /// </summary>
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
        public Priority Priority { get; set; } = Priority.Medium;
        public DateTime? DueDate { get; set; }
        public List<TreeNode> Children { get; set; } = [];
    }
    private sealed class ListItemLink
    {
        /// <summary>
        /// Represents a link between a ToDo list and a ToDo item in GunDB.
        /// Contains the item ID to establish the relationship.
        /// </summary>
        public String ItemId  { get; set; } = string.Empty;
    }
    private sealed class ListChildLink
    {
        /// <summary>
        /// Represents a link between a parent ToDo list and a child ToDo list in GunDB.
        /// Contains the child list ID to establish the hierarchy.
        /// </summary>
        public String ChildListId  { get; set; } = string.Empty;
    }
}