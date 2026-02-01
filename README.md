# Disclaimer
This project is not affiliated with Steam or any other connected Source.
This project is also not an official Software from Steam.

-This Discord Bot cant be added to Guilds/Servers 

# SteamWishlistChecker
This Discord Bot uses the Steam Web API to check for the stated SteamID's related Account Wishlist and sends notification via direct Messages when a game or app on said wishlist has the highest discount since start of this service.

I build this since SteamDB does not have an API and wanted to save some time on checking games prices.

### Usage
- Once u authorised the Bot to your Account and stated your SteamID with the /setsteam command, the bot will check your Wishlist daily to not miss any discount and message you, when it drops below or equal the last saved amount since starting of this service.
- Since Users can have the same games in their wishlist, some games may even already have entries. 

## Privacy Policy

### Collection of Data
- Using this bot following data is collected:
    - Stated SteamID
    - Games from wishlist associated with the Steam Account from the SteamID
    - Prices of the games from wishlist

### Usage of Data
- The Data is used to check for the lowest price of each game inside the Steam Accounts wishlist
- The price checks of each game are not paired with the Steam Account
- The User gets notified via Discord when a game on the associated wishlist is hitting its lowest price since recording

### Lifetime of Data
- The stated SteamID must be stored permanently to ensure correct working process 
- The Collected SteamID Data is not saved and is used less than 1h (depending on the number of request)
- Game Prices are stored permanently without connection to any SteamID or Account

### Sharing of Data
- The stored Data is not willingly shared with any outside party

### Serverregion
- The Server is running in EU

### Request for Data deletion
- Once u removed your SteamID via the Discord Bot command, your data is permanently erased