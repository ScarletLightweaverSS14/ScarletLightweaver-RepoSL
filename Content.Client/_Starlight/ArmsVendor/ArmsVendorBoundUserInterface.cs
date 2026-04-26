using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client._Starlight.ArmsVendor.UI;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._Starlight.ArmsVendor;

[UsedImplicitly]
public sealed class ArmsVendorBoundUserInterface : BoundUserInterface
{
    private ArmsVendorMenu? _menu;

    private HashSet<ListingDataWithCostModifiers> _listings = new();
    private ProtoId<StoreCategoryPrototype>? _currentCategory;

    public ArmsVendorBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<ArmsVendorMenu>();

        _menu.OnBuyPressed += listing =>
        {
            SendMessage(new StoreBuyListingMessage(listing.ID));
        };

        _menu.OnCategorySelected += category =>
        {
            _currentCategory = category;
            UpdateItemList();
        };
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not StoreUpdateState msg)
            return;

        _listings = msg.Listings;

        _menu?.UpdateBalance(msg.Balance);

        // Collect the ordered, distinct category IDs present in our listings
        var categoryIds = _listings
            .SelectMany(l => l.Categories)
            .Distinct()
            .ToList();

        _menu?.PopulateCategories(categoryIds);

        // Re-populate items if a category is already selected
        if (_currentCategory.HasValue)
            UpdateItemList();
    }

    private void UpdateItemList()
    {
        if (_menu == null)
            return;

        if (!_currentCategory.HasValue)
        {
            _menu.PopulateItems(Enumerable.Empty<ListingDataWithCostModifiers>());
            return;
        }

        var items = _listings
            .Where(l => l.Categories.Contains(_currentCategory.Value))
            .OrderBy(l => l.OriginalCost.TryGetValue("ArmsTicket", out var c) ? (float)c : 0f)
            .ToList();

        _menu.PopulateItems(items);
    }
}
