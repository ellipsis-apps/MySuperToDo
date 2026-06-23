# GunDB Initialization with Seed Management

## Overview

This implementation provides a complete GunDB initialization flow with seed management. When the app starts, it checks if a GunDB seed exists. If not, it guides the user through a multi-step process to either enter an existing seed or create a new one.

## Components

### Services

#### `GunDbSeedService` (MySuperToDo/Services/GunDbSeedService.cs)
Manages all seed-related operations:
- **GetStoredSeedAsync()** - Retrieves the stored seed from local storage
- **SeedExistsAsync()** - Checks if a seed is already stored
- **GenerateNewSeed()** - Creates a new random base64-encoded seed
- **StoreSeedAsync(seed)** - Persists a seed to local storage
- **DeleteAllAsync()** - Completely wipes seed and IndexedDB database
- **IsValidSeed(seed)** - Validates seed format

### Pages

#### `App.razor` (MySuperToDo/App.razor)
- Root component that checks GunDB initialization status
- Shows a loading spinner while checking
- Routes to `SeedInitialization` if no seed exists
- Shows normal router with authentication when initialized

#### `SeedInitialization.razor` (MySuperToDo/Pages/SeedInitialization.razor)
- Orchestrator component for the initialization flow
- Manages states: Entry → Display → Initializing → Complete/Error
- Handles seed input, validation, and storage
- Coordinates with sub-components

#### `SeedEntry.razor` (MySuperToDo/Pages/SeedEntry.razor)
- Allows user to choose between entering existing seed or creating new one
- Uses radio buttons to toggle between modes
- Delegates to sub-components: `ExistingSeedInput` or `NewSeedConfirmation`

#### `ExistingSeedInput.razor` (MySuperToDo/Pages/ExistingSeedInput.razor)
- Input form for existing seeds
- Validates seed format before submission
- Shows validation errors
- Provides "Continue" button

#### `NewSeedConfirmation.razor` (MySuperToDo/Pages/NewSeedConfirmation.razor)
- Educational page explaining seed importance
- Requires user checkbox confirmation before creation
- Ensures user understands they must save the seed

#### `SeedDisplay.razor` (MySuperToDo/Pages/SeedDisplay.razor)
- Shows generated seed in a monospace font
- "Copy to Clipboard" button for easy sharing
- Requires user confirmation they saved the seed
- Large warning box highlighting importance of seed security

## User Flow

### First Time Setup (No seed exists)

1. **App Initialization**
   - App.razor checks `SeedService.SeedExistsAsync()`
   - If no seed found, renders `SeedInitialization` component

2. **Seed Entry**
   - User chooses: "Use Existing Seed" or "Create New Seed"
   - If existing: Enters seed and validates
   - If new: Confirms understanding of importance

3. **Seed Display** (only for new seeds)
   - Seed displayed in copy-friendly format
   - User copies seed to secure location
   - User confirms they saved it

4. **Initialization**
   - Seed stored in local storage
   - GunDB database created with seed
   - App navigates to home page
   - Normal authentication and routing resumes

### Existing Setup (Seed exists)

1. **App Initialization**
   - App.razor checks `SeedService.SeedExistsAsync()`
   - Seed found, shows normal router
   - User proceeds with authentication

## Database Deletion

Users can delete the GunDB database via the delete button available in:
- `AllItems.razor` component
- `Lists.razor` component

### Deletion Flow

1. Confirmation dialog asks for consent
2. If confirmed: `GunDbSeedService.DeleteAllAsync()` is called
3. Process:
   - Deletes seed from local storage
   - Deletes IndexedDB 'gun' database
   - Removes all associated data
4. Page reloads to re-initialize app
5. User sees initialization flow again

## Integration

### Program.cs
```csharp
// GunDB seed management
builder.Services.AddScoped<GunDbSeedService>();
```

### Index Components (AllItems, Lists)
```csharp
[Inject] public GunDbSeedService GunDbSeedService { get; set; } = default!;

private async Task DeleteGunDBDatabaseAsync()
{
	// ... confirmation dialog ...
	await GunDbSeedService.DeleteAllAsync();
	await JSRuntime.InvokeVoidAsync("location.reload");
}
```

## Security Considerations

1. **Seed Storage**: Seeds are stored in browser local storage (Blazored.LocalStorage)
2. **Seed Display**: Only shown once during setup; user must copy and save
3. **Validation**: Seeds validated as base64 format
4. **Deletion**: Complete data wipe available to users
5. **IndexedDB Access**: No direct JavaScript manipulation; handled through service

## Configuration

No appsettings configuration required. All defaults are built-in:
- Local storage key: `"gundb_seed"` (internal constant)
- Seed length: 32 bytes (256 bits) for cryptographic security
- Base64 encoding for cross-platform compatibility

## Future Enhancements

1. **Seed Export/Import**: Allow users to export/import seeds for backup
2. **Multiple Seeds**: Support multiple identities
3. **Seed Encryption**: Encrypt seeds with password before storage
4. **Seed Recovery**: QR codes or other mechanisms for seed backup
5. **Cloud Sync**: Optional seed sync to cloud storage with encryption

## Troubleshooting

### "Invalid seed format" error
- Ensure seed is a valid base64 string
- Copy-paste carefully; avoid extra whitespace

### Database not initializing after seed entry
- Check browser console for errors
- Verify seed was stored in local storage
- Check browser IndexedDB is accessible

### Can't delete database
- Ensure confirmation is clicked
- Check browser console for JavaScript errors
- Try manual local storage/IndexedDB deletion via browser dev tools

## Testing

Key test scenarios:
1. First launch with no seed → Should show initialization
2. Enter invalid seed → Should show error
3. Create new seed → Should display seed and require confirmation
4. Complete initialization → Should navigate to home
5. Delete database → Should return to initialization on reload
