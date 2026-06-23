# Encrypted GunDB Seed Integration - Implementation Guide

## Overview
GunDB seed is now encrypted with a user password and stored in IndexedDB instead of plain-text localStorage. This provides secure identity persistence across browser sessions.

## Architecture

### 1. Encryption Layer (gun-interop.js)
**Functions:**
- `encryptSeed(seed, password)` - AES-GCM encryption with random IV
- `decryptSeed(encryptedData, password)` - AES-GCM decryption
- `storeSeedInIndexedDB(encryptedSeed)` - Stores encrypted seed in IndexedDB
- `getSeedFromIndexedDB()` - Retrieves encrypted seed from IndexedDB
- `deleteSeedFromIndexedDB()` - Removes encrypted seed from IndexedDB

**Encryption Details:**
- Algorithm: AES-GCM (256-bit key)
- Key derivation: PBKDF2 with 100,000 iterations
- IV: 96-bit random (unique per encryption)
- Storage: In IndexedDB under `MySuperToDo` database, `gundb-seed` store

### 2. Service Layer (GunDbSeedService.cs)
**Updated Methods:**
- `GetStoredSeedAsync(password)` - Requires password to decrypt
- `StoreSeedAsync(seed, password)` - Encrypts and stores seed
- `SeedExistsAsync()` - Checks if encrypted seed exists
- `DeleteAllAsync()` - Removes encrypted seed and Gun database
- `GenerateNewSeed()` - Creates new random seed
- `IsValidSeed(seed)` - Validates seed format

### 3. User Flows

#### First-Time Setup
1. App detects no encrypted seed
2. Redirects to `/seed-initialization`
3. User chooses: "Use Existing Seed" or "Create New Seed"

**New Seed Path:**
1. User confirms they understand this seeds importance
2. User creates a password (minimum 6 characters)
3. System generates random seed
4. Seed displayed for backup/screenshot
5. User confirms password
6. Seed encrypted with password and stored in IndexedDB
7. GunDB initialized with seed
8. App routed to main interface

**Existing Seed Path:**
1. User pastes their existing seed
2. Optionally sets a new password
3. Seed encrypted and stored
4. GunDB initialized
5. App routed to main interface

#### Return Visits
1. App checks if encrypted seed exists
2. Shows `PasswordPrompt.razor` component
3. User enters password
4. Seed decrypted in memory
5. GunDB initialized with decrypted seed
6. App proceeds to main interface
7. Decrypted seed never stored in plain text

#### Database Deletion
1. User clicks "Delete Database"
2. Calls `GunDbSeedService.DeleteAllAsync()`
3. Encrypted seed removed from IndexedDB
4. Gun database removed from IndexedDB
5. User can create new seed on next visit

## Security Properties

✅ **Seed Encryption:** Seed never stored in plain text
✅ **Password Protection:** Uses PBKDF2 key derivation (100k iterations)
✅ **Random IV:** Each encryption uses a unique 96-bit IV
✅ **In-Memory Only:** Decrypted seed exists only in memory during session
✅ **IndexedDB Storage:** Leverages browser's native encrypted storage
✅ **No Password Storage:** Passwords not persisted anywhere

## Potential Improvements

1. **Random Salt per Seed:** Current implementation uses fixed salt for simplicity
   - To improve: Store a random salt per encrypted seed in IndexedDB
   - Adds minimal overhead, significantly improves security

2. **Biometric Unlock:** Optional fingerprint/face recognition on returning users
   - Could reduce password entry friction

3. **Seed Backup Recovery:** Server-side seed backup with user passphrase
   - Would require backend infrastructure
   - Enables multi-device setup

4. **Key Rotation:** Ability to re-encrypt seed with new password
   - Add method: `ReEncryptSeedAsync(oldPassword, newPassword)`

5. **Seed Expiry:** Optional seed invalidation after N days
   - Security enhancement for sensitive data

## Testing Checklist

- [ ] Create new seed on first visit
  - [ ] Password entry validation works
  - [ ] Seed displays correctly
  - [ ] Encrypted seed appears in IndexedDB (devtools)
  - [ ] GunDB initializes with identity

- [ ] Return visit with existing encrypted seed
  - [ ] Password prompt appears
  - [ ] Correct password unlocks database
  - [ ] Incorrect password shows error and allows retry
  - [ ] Decrypted seed initializes GunDB

- [ ] Database deletion
  - [ ] Delete button removes seed and Gun database
  - [ ] Both IndexedDB entries are cleared
  - [ ] Next visit shows seed initialization flow

- [ ] Password edge cases
  - [ ] Empty password rejected
  - [ ] Mismatched passwords rejected
  - [ ] Minimum 6 characters enforced
  - [ ] Special characters accepted

- [ ] Cross-browser functionality
  - [ ] Works in Chrome/Edge (Chromium)
  - [ ] Works in Firefox
  - [ ] Works in Safari

## Files Modified

- `wwwroot/js/gun-interop.js` - Added encryption/decryption functions
- `Services/GunDbSeedService.cs` - Updated to use encrypted IndexedDB
- `Pages/SeedEntry.razor` - Added password entry for new seeds
- `Pages/SeedInitialization.razor` - Updated flow for password handling
- `Pages/PasswordPrompt.razor` - NEW component for startup unlock
- `Pages/App.razor` - Updated to detect seed and show password prompt
- `Application/Interfaces/IGunDbService.cs` - Added SetSeed method
- `Services/GunDbService.cs` - Passes seed to JS interop

## Deployment Notes

1. Existing users with plain-text seeds in localStorage will need to:
   - Re-enter their seed during initialization flow
   - Set a password for encryption
   - Seed will be re-encrypted and stored

2. No migration script needed - old localStorage seeds are ignored

3. Clear browser cache/storage if transitioning from old implementation

## References

- [SubtleCrypto API](https://developer.mozilla.org/en-US/docs/Web/API/SubtleCrypto)
- [PBKDF2](https://tools.ietf.org/html/rfc2898)
- [AES-GCM](https://en.wikipedia.org/wiki/Galois/Counter_Mode)
- [IndexedDB Storage](https://developer.mozilla.org/en-US/docs/Web/API/IndexedDB_API)
