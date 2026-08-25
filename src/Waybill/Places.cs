using System;
using System.Collections.Generic;

namespace Waybill;

/// <summary>
/// Which state or country a city is in.
///
/// The games do not say. Telemetry gives the city and the company and nothing about
/// the region either sits in, so this is a table, written out by hand and keyed by
/// the name the game reports.
///
/// It is deliberately incomplete. A city that is not in it is shown as the game
/// named it, with nothing added, which is what happens for every map mod ever
/// written and for a handful of names the table refuses to guess at: American Truck
/// Simulator has a Salina in Utah and another in Kansas, and the name on its own
/// cannot tell them apart. A missing code says "not known"; a wrong one would say
/// something false about a delivery, which is worse than saying nothing.
/// </summary>
public static class Places {
    /// <summary>The city with its region, when the region is known: "Yakima, WA".
    /// The city on its own otherwise.</summary>
    public static string Say(string game, string city) =>
        Code(game, city) is { } code ? $"{city}, {code}" : city;

    /// <summary>Just the code, for the places that lay it out themselves.</summary>
    public static string? Code(string game, string city) {
        if (string.IsNullOrWhiteSpace(city)) return null;
        var table = game.Equals("Ats", StringComparison.OrdinalIgnoreCase) ? Ats
                  : game.Equals("Ets2", StringComparison.OrdinalIgnoreCase) ? Ets2
                  : null;
        return table != null && table.TryGetValue(city.Trim(), out var code) ? code : null;
    }

    // ---------- American Truck Simulator: the state ----------

    private static readonly Dictionary<string, string> Ats = new(StringComparer.OrdinalIgnoreCase) {
        // California
        ["Bakersfield"] = "CA", ["Barstow"] = "CA", ["El Centro"] = "CA", ["Eureka"] = "CA",
        ["Fresno"] = "CA", ["Huron"] = "CA", ["Los Angeles"] = "CA", ["Oakland"] = "CA",
        ["Oxnard"] = "CA", ["Redding"] = "CA", ["Sacramento"] = "CA", ["San Diego"] = "CA",
        ["San Francisco"] = "CA", ["San Rafael"] = "CA", ["Santa Cruz"] = "CA",
        ["Santa Maria"] = "CA", ["Stockton"] = "CA", ["Truckee"] = "CA", ["Ukiah"] = "CA",
        // Nevada
        ["Carson City"] = "NV", ["Elko"] = "NV", ["Ely"] = "NV", ["Fallon"] = "NV",
        ["Jackpot"] = "NV", ["Las Vegas"] = "NV", ["Pioche"] = "NV", ["Primm"] = "NV",
        ["Reno"] = "NV", ["Tonopah"] = "NV", ["Winnemucca"] = "NV",
        // Arizona
        ["Camp Verde"] = "AZ", ["Clifton"] = "AZ", ["Ehrenberg"] = "AZ", ["Flagstaff"] = "AZ",
        ["Grand Canyon Village"] = "AZ", ["Holbrook"] = "AZ", ["Kayenta"] = "AZ",
        ["Kingman"] = "AZ", ["Nogales"] = "AZ", ["Page"] = "AZ", ["Phoenix"] = "AZ",
        ["San Simon"] = "AZ", ["Show Low"] = "AZ", ["Sierra Vista"] = "AZ", ["Tucson"] = "AZ",
        ["Winslow"] = "AZ", ["Yuma"] = "AZ",
        // New Mexico
        ["Alamogordo"] = "NM", ["Albuquerque"] = "NM", ["Artesia"] = "NM", ["Carlsbad"] = "NM",
        ["Clovis"] = "NM", ["Farmington"] = "NM", ["Gallup"] = "NM", ["Hobbs"] = "NM",
        ["Las Cruces"] = "NM", ["Raton"] = "NM", ["Roswell"] = "NM", ["Santa Fe"] = "NM",
        ["Socorro"] = "NM", ["Tucumcari"] = "NM",
        // Oregon
        ["Astoria"] = "OR", ["Bend"] = "OR", ["Burns"] = "OR", ["Coos Bay"] = "OR",
        ["Eugene"] = "OR", ["Klamath Falls"] = "OR", ["Lakeview"] = "OR", ["Medford"] = "OR",
        ["Newport"] = "OR", ["Ontario"] = "OR", ["Pendleton"] = "OR", ["Portland"] = "OR",
        ["Salem"] = "OR", ["The Dalles"] = "OR",
        // Washington
        ["Aberdeen"] = "WA", ["Bellingham"] = "WA", ["Colville"] = "WA", ["Everett"] = "WA",
        ["Grand Coulee"] = "WA", ["Kennewick"] = "WA", ["Longview"] = "WA", ["Olympia"] = "WA",
        ["Omak"] = "WA", ["Port Angeles"] = "WA", ["Seattle"] = "WA", ["Spokane"] = "WA",
        ["Tacoma"] = "WA", ["Vancouver"] = "WA", ["Wenatchee"] = "WA", ["Yakima"] = "WA",
        // Utah
        ["Blanding"] = "UT", ["Cedar City"] = "UT", ["Green River"] = "UT", ["Logan"] = "UT",
        ["Moab"] = "UT", ["Monticello"] = "UT", ["Ogden"] = "UT", ["Price"] = "UT",
        ["Provo"] = "UT", ["Salt Lake City"] = "UT", ["St. George"] = "UT", ["Vernal"] = "UT",
        // Idaho
        ["Boise"] = "ID", ["Coeur d'Alene"] = "ID", ["Idaho Falls"] = "ID", ["Ketchum"] = "ID",
        ["Lewiston"] = "ID", ["Pocatello"] = "ID", ["Salmon"] = "ID", ["Sandpoint"] = "ID",
        ["Twin Falls"] = "ID",
        // Colorado
        ["Alamosa"] = "CO", ["Colorado Springs"] = "CO", ["Craig"] = "CO", ["Denver"] = "CO",
        ["Durango"] = "CO", ["Fort Collins"] = "CO", ["Grand Junction"] = "CO",
        ["Gunnison"] = "CO", ["Lamar"] = "CO", ["Limon"] = "CO", ["Montrose"] = "CO",
        ["Pueblo"] = "CO", ["Rangely"] = "CO", ["Sterling"] = "CO", ["Trinidad"] = "CO",
        // Wyoming
        ["Casper"] = "WY", ["Cheyenne"] = "WY", ["Cody"] = "WY", ["Evanston"] = "WY",
        ["Gillette"] = "WY", ["Jackson"] = "WY", ["Laramie"] = "WY", ["Rawlins"] = "WY",
        ["Rock Springs"] = "WY", ["Sheridan"] = "WY",
        // Montana
        ["Billings"] = "MT", ["Bozeman"] = "MT", ["Butte"] = "MT", ["Great Falls"] = "MT",
        ["Havre"] = "MT", ["Helena"] = "MT", ["Kalispell"] = "MT", ["Miles City"] = "MT",
        ["Missoula"] = "MT",
        // Texas
        ["Amarillo"] = "TX", ["Austin"] = "TX", ["Dallas"] = "TX", ["El Paso"] = "TX",
        ["Fort Worth"] = "TX", ["Galveston"] = "TX", ["Houston"] = "TX", ["Laredo"] = "TX",
        ["Lubbock"] = "TX", ["Odessa"] = "TX", ["San Antonio"] = "TX", ["Victoria"] = "TX",
        // Oklahoma
        ["Ardmore"] = "OK", ["Enid"] = "OK", ["Guymon"] = "OK", ["Lawton"] = "OK",
        ["McAlester"] = "OK", ["Oklahoma City"] = "OK", ["Tulsa"] = "OK", ["Woodward"] = "OK",
        // Kansas
        ["Colby"] = "KS", ["Dodge City"] = "KS", ["Garden City"] = "KS", ["Great Bend"] = "KS",
        ["Hays"] = "KS", ["Hutchinson"] = "KS", ["Liberal"] = "KS", ["Manhattan"] = "KS",
        ["Topeka"] = "KS", ["Wichita"] = "KS",
        // Nebraska
        ["Grand Island"] = "NE", ["Kearney"] = "NE", ["Lincoln"] = "NE", ["Norfolk"] = "NE",
        ["North Platte"] = "NE", ["Omaha"] = "NE", ["Scottsbluff"] = "NE",
        // Arkansas and Missouri
        ["Fayetteville"] = "AR", ["Fort Smith"] = "AR", ["Jonesboro"] = "AR",
        ["Little Rock"] = "AR", ["Pine Bluff"] = "AR",
        ["Cape Girardeau"] = "MO", ["Columbia"] = "MO", ["Jefferson City"] = "MO",
        ["Joplin"] = "MO", ["Springfield"] = "MO", ["St. Joseph"] = "MO", ["St. Louis"] = "MO",
    };

    // ---------- Euro Truck Simulator 2: the country ----------

    private static readonly Dictionary<string, string> Ets2 = new(StringComparer.OrdinalIgnoreCase) {
        // Germany
        ["Aachen"] = "DE", ["Berlin"] = "DE", ["Bremen"] = "DE", ["Cologne"] = "DE",
        ["Dortmund"] = "DE", ["Dresden"] = "DE", ["Duisburg"] = "DE", ["Düsseldorf"] = "DE",
        ["Erfurt"] = "DE", ["Frankfurt am Main"] = "DE", ["Hamburg"] = "DE", ["Hannover"] = "DE",
        ["Kassel"] = "DE", ["Kiel"] = "DE", ["Leipzig"] = "DE", ["Magdeburg"] = "DE",
        ["Mannheim"] = "DE", ["München"] = "DE", ["Munich"] = "DE", ["Nürnberg"] = "DE",
        ["Nuremberg"] = "DE", ["Osnabrück"] = "DE", ["Rostock"] = "DE", ["Stuttgart"] = "DE",
        ["Travemünde"] = "DE", ["Werlte"] = "DE",
        // Austria and Switzerland
        ["Graz"] = "AT", ["Innsbruck"] = "AT", ["Linz"] = "AT", ["Salzburg"] = "AT",
        ["Wien"] = "AT", ["Vienna"] = "AT",
        ["Bern"] = "CH", ["Genève"] = "CH", ["Geneva"] = "CH", ["Zürich"] = "CH",
        ["Zurich"] = "CH",
        // The Low Countries
        ["Amsterdam"] = "NL", ["Den Haag"] = "NL", ["Eindhoven"] = "NL", ["Groningen"] = "NL",
        ["Nijmegen"] = "NL", ["Rotterdam"] = "NL", ["Utrecht"] = "NL",
        ["Antwerpen"] = "BE", ["Brussel"] = "BE", ["Brussels"] = "BE", ["Charleroi"] = "BE",
        ["Gent"] = "BE", ["Liège"] = "BE", ["Oostende"] = "BE",
        ["Luxembourg"] = "LU",
        // France
        ["Ajaccio"] = "FR", ["Bastia"] = "FR", ["Bordeaux"] = "FR", ["Brest"] = "FR",
        ["Calais"] = "FR", ["Clermont-Ferrand"] = "FR", ["Dijon"] = "FR", ["Le Havre"] = "FR",
        ["Lille"] = "FR", ["Limoges"] = "FR", ["Lyon"] = "FR", ["Marseille"] = "FR",
        ["Metz"] = "FR", ["Montpellier"] = "FR", ["Nantes"] = "FR", ["Nice"] = "FR",
        ["Paris"] = "FR", ["Reims"] = "FR", ["Rennes"] = "FR", ["Roscoff"] = "FR",
        ["Strasbourg"] = "FR", ["Toulouse"] = "FR",
        // Italy
        ["Ancona"] = "IT", ["Bari"] = "IT", ["Bologna"] = "IT", ["Cagliari"] = "IT",
        ["Catania"] = "IT", ["Firenze"] = "IT", ["Genova"] = "IT", ["Livorno"] = "IT",
        ["Messina"] = "IT", ["Milano"] = "IT", ["Napoli"] = "IT", ["Palermo"] = "IT",
        ["Parma"] = "IT", ["Pescara"] = "IT", ["Roma"] = "IT", ["Suzzara"] = "IT",
        ["Taranto"] = "IT", ["Torino"] = "IT", ["Venezia"] = "IT", ["Verona"] = "IT",
        // Iberia
        ["A Coruña"] = "ES", ["Algeciras"] = "ES", ["Almería"] = "ES", ["Badajoz"] = "ES",
        ["Barcelona"] = "ES", ["Bilbao"] = "ES", ["Burgos"] = "ES", ["Cádiz"] = "ES",
        ["Gijón"] = "ES", ["Granada"] = "ES", ["Huelva"] = "ES", ["Madrid"] = "ES",
        ["Málaga"] = "ES", ["Murcia"] = "ES", ["Pamplona"] = "ES", ["Salamanca"] = "ES",
        ["Sevilla"] = "ES", ["Valencia"] = "ES", ["Valladolid"] = "ES", ["Zaragoza"] = "ES",
        ["Braga"] = "PT", ["Coimbra"] = "PT", ["Faro"] = "PT", ["Lisboa"] = "PT",
        ["Porto"] = "PT", ["Setúbal"] = "PT",
        // The United Kingdom
        ["Aberdeen"] = "GB", ["Birmingham"] = "GB", ["Cambridge"] = "GB", ["Cardiff"] = "GB", ["Carlisle"] = "GB",
        ["Dover"] = "GB", ["Edinburgh"] = "GB", ["Felixstowe"] = "GB", ["Glasgow"] = "GB",
        ["Grimsby"] = "GB", ["Liverpool"] = "GB", ["London"] = "GB", ["Manchester"] = "GB",
        ["Newcastle-upon-Tyne"] = "GB", ["Plymouth"] = "GB", ["Sheffield"] = "GB",
        ["Southampton"] = "GB", ["Swansea"] = "GB",
        // Poland, Czechia, Slovakia, Hungary
        ["Białystok"] = "PL", ["Gdańsk"] = "PL", ["Katowice"] = "PL", ["Kraków"] = "PL",
        ["Lublin"] = "PL", ["Łódź"] = "PL", ["Olsztyn"] = "PL", ["Poznań"] = "PL",
        ["Szczecin"] = "PL", ["Warszawa"] = "PL", ["Wrocław"] = "PL",
        ["Brno"] = "CZ", ["Ostrava"] = "CZ", ["Plzeň"] = "CZ", ["Praha"] = "CZ",
        ["Banská Bystrica"] = "SK", ["Bratislava"] = "SK", ["Košice"] = "SK",
        ["Budapest"] = "HU", ["Debrecen"] = "HU", ["Pécs"] = "HU", ["Szeged"] = "HU",
        // Scandinavia
        ["Aalborg"] = "DK", ["Esbjerg"] = "DK", ["Frederikshavn"] = "DK", ["Gedser"] = "DK",
        ["Hirtshals"] = "DK", ["København"] = "DK", ["Odense"] = "DK",
        ["Göteborg"] = "SE", ["Helsingborg"] = "SE", ["Jönköping"] = "SE", ["Kalmar"] = "SE",
        ["Kapellskär"] = "SE", ["Linköping"] = "SE", ["Luleå"] = "SE", ["Malmö"] = "SE",
        ["Örebro"] = "SE", ["Stockholm"] = "SE", ["Södertälje"] = "SE", ["Umeå"] = "SE",
        ["Uppsala"] = "SE", ["Västerås"] = "SE", ["Växjö"] = "SE",
        ["Bergen"] = "NO", ["Kristiansand"] = "NO", ["Oslo"] = "NO", ["Stavanger"] = "NO",
        ["Trondheim"] = "NO",
        ["Helsinki"] = "FI", ["Kouvola"] = "FI", ["Naantali"] = "FI", ["Turku"] = "FI",
        // The Baltic states
        ["Pärnu"] = "EE", ["Tallinn"] = "EE", ["Tartu"] = "EE",
        ["Daugavpils"] = "LV", ["Liepāja"] = "LV", ["Rīga"] = "LV", ["Ventspils"] = "LV",
        ["Kaunas"] = "LT", ["Klaipėda"] = "LT", ["Panevėžys"] = "LT", ["Vilnius"] = "LT",
        // The Black Sea
        ["Brașov"] = "RO", ["București"] = "RO", ["Cluj-Napoca"] = "RO", ["Constanța"] = "RO",
        ["Craiova"] = "RO", ["Galați"] = "RO", ["Iași"] = "RO", ["Timișoara"] = "RO",
        ["Burgas"] = "BG", ["Pleven"] = "BG", ["Plovdiv"] = "BG", ["Ruse"] = "BG",
        ["Sofia"] = "BG", ["Varna"] = "BG",
        ["Edirne"] = "TR", ["Istanbul"] = "TR", ["İstanbul"] = "TR", ["Tekirdağ"] = "TR",
    };
}
