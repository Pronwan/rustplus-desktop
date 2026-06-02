import { serve } from "https://deno.land/std@0.168.0/http/server.ts"
import { createClient } from "https://esm.sh/@supabase/supabase-js@2.7.1"

// ────────────────────────────────────────────────────────────────────────────
// BIP39 English wordlist (2048 words, standard)
// ────────────────────────────────────────────────────────────────────────────
const WORDS = [
  "abandon","ability","able","about","above","absent","absorb","abstract","absurd","abuse",
  "access","accident","account","accuse","achieve","acid","acoustic","acquire","across","act",
  "action","actor","actress","actual","adapt","add","addict","address","adjust","admit",
  "adult","advance","advice","aerobic","affair","afford","afraid","again","age","agent",
  "agree","ahead","aim","air","airport","aisle","alarm","album","alcohol","alert",
  "alien","all","alley","allow","almost","alone","alpha","already","also","alter",
  "always","amateur","amazing","among","amount","amused","analyst","anchor","ancient","anger",
  "angle","angry","animal","ankle","announce","annual","another","answer","antenna","antique",
  "anxiety","any","apart","apology","appear","apple","approve","april","arch","arctic",
  "area","arena","argue","arm","armed","armor","army","around","arrange","arrest",
  "arrive","arrow","art","artefact","artist","artwork","ask","aspect","assault","asset",
  "assist","assume","asthma","athlete","atom","attack","attend","attitude","attract","auction",
  "audit","august","aunt","author","auto","autumn","average","avocado","avoid","awake",
  "aware","away","awesome","awful","awkward","axis","baby","bachelor","bacon","badge",
  "bag","balance","balcony","ball","bamboo","banana","banner","bar","barely","bargain",
  "barrel","base","basic","basket","battle","beach","bean","beauty","because","become",
  "beef","before","begin","behave","behind","believe","below","belt","bench","benefit",
  "best","betray","better","between","beyond","bicycle","bid","bike","bind","biology",
  "bird","birth","bitter","black","blade","blame","blanket","blast","bleak","bless",
  "blind","blood","blossom","blouse","blue","blur","blush","board","boat","body",
  "boil","bomb","bone","bonus","book","boost","border","boring","borrow","boss",
  "bottom","bounce","box","boy","bracket","brain","brand","brass","brave","bread",
  "breeze","brick","bridge","brief","bright","bring","brisk","broccoli","broken","bronze",
  "broom","brother","brown","brush","bubble","buddy","budget","buffalo","build","bulb",
  "bulk","bullet","bundle","bunker","burden","burger","burst","bus","business","busy",
  "butter","buyer","buzz","cabbage","cabin","cable","cactus","cage","cake","call",
  "calm","camera","camp","can","canal","cancel","candy","cannon","canoe","canvas",
  "canyon","capable","capital","captain","car","carbon","card","cargo","carpet","carry",
  "cart","case","cash","casino","castle","casual","cat","catalog","catch","category",
  "cattle","caught","cause","caution","cave","ceiling","celery","cement","census","century",
  "cereal","certain","chair","chalk","champion","change","chaos","chapter","charge","chase",
  "chat","cheap","check","cheese","chef","cherry","chest","chicken","chief","child",
  "chimney","choice","choose","chronic","chuckle","chunk","churn","cigar","cinnamon","circle",
  "citizen","city","civil","claim","clap","clarify","claw","clay","clean","clerk",
  "clever","click","client","cliff","climb","clinic","clip","clock","clog","close",
  "cloth","cloud","clown","club","clump","cluster","clutch","coach","coast","coconut",
  "code","coffee","coil","coin","collect","color","column","combine","come","comfort",
  "comic","common","company","concert","conduct","confirm","congress","connect","consider","control",
  "convince","cook","cool","copper","copy","coral","core","corn","correct","cost",
  "cotton","couch","country","couple","course","cousin","cover","coyote","crack","cradle",
  "craft","cram","crane","crash","crater","crawl","crazy","cream","credit","creek",
  "crew","cricket","crime","crisp","critic","crop","cross","crouch","crowd","crucial",
  "cruel","cruise","crumble","crunch","crush","cry","crystal","cube","culture","cup",
  "cupboard","curious","current","curtain","curve","cushion","custom","cute","cycle","dad",
  "damage","damp","dance","danger","daring","dash","daughter","dawn","day","deal",
  "debate","debris","decade","december","decide","decline","decorate","decrease","deer","defense",
  "define","defy","degree","delay","deliver","demand","demise","denial","dentist","deny",
  "depart","depend","deposit","depth","deputy","derive","describe","desert","design","desk",
  "despair","destroy","detail","detect","develop","device","devote","diagram","dial","diamond",
  "diary","dice","diesel","diet","differ","digital","dignity","dilemma","dinner","dinosaur",
  "direct","dirt","disagree","discover","disease","dish","dismiss","disorder","display","distance",
  "divert","divide","divorce","dizzy","doctor","document","dog","doll","dolphin","domain",
  "donate","donkey","donor","door","dose","double","dove","draft","dragon","drama",
  "drastic","draw","dream","dress","drift","drill","drink","drip","drive","drop",
  "drum","dry","duck","dumb","dune","during","dust","dutch","duty","dwarf",
  "dynamic","eager","eagle","early","earn","earth","easily","east","easy","echo",
  "ecology","economy","edge","edit","educate","effort","egg","eight","either","elbow",
  "elder","electric","elegant","element","elephant","elevator","elite","else","embark","embody",
  "embrace","emerge","emotion","employ","empower","empty","enable","enact","end","endless",
  "endorse","enemy","energy","enforce","engage","engine","enhance","enjoy","enlist","enough",
  "enrich","enroll","ensure","enter","entire","entry","envelope","episode","equal","equip",
  "era","erase","erode","erosion","error","erupt","escape","essay","essence","estate",
  "eternal","ethics","evidence","evil","evoke","evolve","exact","example","excess","exchange",
  "excite","exclude","excuse","execute","exercise","exhaust","exhibit","exile","exist","exit",
  "exotic","expand","expect","expire","explain","expose","extend","extra","eye","eyebrow",
  "fabric","face","faculty","fade","faint","faith","fall","false","fame","family",
  "famous","fan","fancy","fantasy","farm","fashion","fat","fatal","father","fatigue",
  "fault","favorite","feature","february","federal","fee","feed","feel","female","fence",
  "festival","fetch","fever","few","fiber","fiction","field","figure","file","film",
  "filter","final","find","fine","finger","finish","fire","firm","first","fiscal",
  "fish","fit","fitness","fix","flag","flame","flash","flat","flavor","flee",
  "flight","flip","float","flock","floor","flower","fluid","flush","fly","foam",
  "focus","fog","foil","fold","follow","food","foot","force","foreign","forest",
  "forget","fork","fortune","forum","forward","fossil","foster","found","fox","fragile",
  "frame","frequent","fresh","friend","fringe","frog","front","frost","frown","frozen",
  "fruit","fuel","fun","funny","furnace","fury","future","gadget","gain","galaxy",
  "gallery","game","gap","garage","garbage","garden","garlic","garment","gas","gasp",
  "gate","gather","gauge","gaze","general","genius","genre","gentle","genuine","gesture",
  "ghost","giant","gift","giggle","ginger","giraffe","girl","give","glad","glance",
  "glare","glass","glide","glimpse","globe","gloom","glory","glove","glow","glue",
  "goat","goddess","gold","good","goose","gorilla","gospel","gossip","govern","gown",
  "grab","grace","grain","grant","grape","grass","gravity","great","green","grid",
  "grief","grit","grocery","group","grow","grunt","guard","guess","guide","guilt",
  "guitar","gun","gym","habit","hair","half","hammer","hamster","hand","happy",
  "harbor","hard","harsh","harvest","hat","have","hawk","hazard","head","health",
  "heart","heavy","hedgehog","height","hello","helmet","help","hen","hero","hidden",
  "high","hill","hint","hip","hire","history","hobby","hockey","hold","hole",
  "holiday","hollow","home","honey","hood","hope","horn","horror","horse","hospital",
  "host","hotel","hour","hover","hub","human","humble","humor","hundred","hungry",
  "hunt","hurdle","hurry","hurt","husband","hybrid","ice","icon","idea","identify",
  "idle","ignore","ill","illegal","illness","image","imitate","immense","immune","impact",
  "impose","improve","impulse","inch","include","income","increase","index","indicate","indoor",
  "industry","infant","inflict","inform","inhale","inherit","initial","inject","injury","inmate",
  "inner","innocent","input","inquiry","insane","insect","inside","inspire","install","intact",
  "interest","into","invest","invite","involve","iron","island","isolate","issue","item",
  "ivory","jacket","jaguar","jar","jazz","jealous","jeans","jelly","jewel","job",
  "join","joke","journey","joy","judge","juice","jump","jungle","junior","junk",
  "just","kangaroo","keen","keep","ketchup","key","kick","kid","kidney","kind",
  "kingdom","kiss","kit","kitchen","kite","kitten","kiwi","knee","knife","knock",
  "know","lab","label","labor","ladder","lady","lake","lamp","language","laptop",
  "large","later","latin","laugh","laundry","lava","law","lawn","lawsuit","layer",
  "lazy","leader","leaf","learn","leave","lecture","left","leg","legal","legend",
  "leisure","lemon","lend","length","lens","leopard","lesson","letter","level","liar",
  "liberty","library","license","life","lift","light","like","limb","limit","link",
  "lion","liquid","list","little","live","lizard","load","loan","lobster","local",
  "lock","logic","lonely","long","loop","lottery","loud","lounge","love","loyal",
  "lucky","luggage","lumber","lunar","lunch","luxury","lyrics","machine","mad","magic",
  "magnet","maid","mail","main","major","make","mammal","man","manage","mandate",
  "mango","mansion","manual","maple","marble","march","margin","marine","market","marriage",
  "mask","mass","master","match","material","math","matrix","matter","maximum","maze",
  "meadow","mean","measure","meat","mechanic","medal","media","melody","melt","member",
  "memory","mention","menu","mercy","merge","merit","merry","mesh","message","metal",
  "method","middle","midnight","milk","million","mimic","mind","minimum","minor","minute",
  "miracle","mirror","misery","miss","mistake","mix","mixed","mixture","mobile","model",
  "modify","mom","moment","monitor","monkey","monster","month","moon","moral","more",
  "morning","mosquito","mother","motion","motor","mountain","mouse","move","movie","much",
  "muffin","mule","multiply","muscle","museum","mushroom","music","must","mutual","myself",
  "mystery","myth","naive","name","napkin","narrow","nasty","nation","nature","near",
  "neck","need","negative","neglect","neither","nephew","nerve","nest","net","network",
  "neutral","never","news","next","nice","night","noble","noise","nominee","noodle",
  "normal","north","nose","notable","note","nothing","notice","novel","now","nuclear",
  "number","nurse","nut","oak","obey","object","oblige","obscure","observe","obtain",
  "obvious","occur","ocean","october","odor","off","offer","office","often","oil",
  "okay","old","olive","olympic","omit","once","one","onion","online","only",
  "open","opera","opinion","oppose","option","orange","orbit","orchard","order","ordinary",
  "organ","orient","original","orphan","ostrich","other","outdoor","outer","output","outside",
  "oval","oven","over","own","owner","oxygen","oyster","ozone","pact","paddle",
  "page","pair","palace","palm","panda","panel","panic","panther","paper","parade",
  "parent","park","parrot","party","pass","patch","path","patient","patrol","pattern",
  "pause","pave","payment","peace","peanut","pear","peasant","pelican","pen","penalty",
  "pencil","people","pepper","perfect","permit","person","pet","phone","photo","phrase",
  "physical","piano","picnic","picture","piece","pig","pigeon","pill","pilot","pink",
  "pioneer","pipe","pistol","pitch","pizza","place","planet","plastic","plate","play",
  "please","pledge","pluck","plug","plunge","poem","poet","point","polar","pole",
  "police","pond","pony","pool","popular","portion","position","possible","post","potato",
  "pottery","poverty","powder","power","practice","praise","predict","prefer","prepare","present",
  "pretty","prevent","price","pride","primary","print","priority","prison","private","prize",
  "problem","process","produce","profit","program","project","promote","proof","property","prosper",
  "protect","proud","provide","public","pudding","pull","pulp","pulse","pumpkin","punch",
  "pupil","puppy","purchase","purity","purpose","purse","push","put","puzzle","pyramid",
  "quality","quantum","quarter","question","quick","quit","quiz","quote","rabbit","raccoon",
  "race","rack","radar","radio","rail","rain","raise","rally","ramp","ranch",
  "random","range","rapid","rare","rate","rather","raven","raw","razor","ready",
  "real","reason","rebel","rebuild","recall","receive","recipe","record","recycle","reduce",
  "reflect","reform","refuse","region","regret","regular","reject","relax","release","relief",
  "rely","remain","remember","remind","remove","render","renew","rent","reopen","repair",
  "repeat","replace","report","require","rescue","resemble","resist","resource","response","result",
  "retire","retreat","return","reunion","reveal","review","reward","rhythm","rib","ribbon",
  "rice","rich","ride","ridge","rifle","right","rigid","ring","riot","ripple",
  "risk","ritual","rival","river","road","roast","robot","robust","rocket","romance",
  "roof","rookie","room","rose","rotate","rough","round","route","royal","rubber",
  "rude","rug","rule","run","runway","rural","sad","saddle","sadness","safe",
  "sail","salad","salmon","salon","salt","salute","same","sample","sand","satisfy",
  "satoshi","sauce","sausage","save","say","scale","scan","scare","scatter","scene",
  "scheme","school","science","scissors","scorpion","scout","scrap","screen","script","scrub",
  "sea","search","season","seat","second","secret","section","security","seed","seek",
  "segment","select","sell","seminar","senior","sense","sentence","series","service","session",
  "settle","setup","seven","shadow","shaft","shallow","share","shed","shell","sheriff",
  "shield","shift","shine","ship","shiver","shock","shoe","shoot","shop","short",
  "shoulder","shove","shrimp","shrug","shuffle","shy","sibling","sick","side","siege",
  "sight","sign","silent","silk","silly","silver","similar","simple","since","sing",
  "siren","sister","situate","six","size","skate","sketch","ski","skill","skin",
  "skirt","skull","slab","slam","sleep","slender","slice","slide","slight","slim",
  "slogan","slot","slow","slush","small","smart","smile","smoke","smooth","snack",
  "snake","snap","sniff","snow","soap","soccer","social","sock","soda","soft",
  "solar","soldier","solid","solution","solve","someone","song","soon","sorry","sort",
  "soul","sound","soup","source","south","space","spare","spatial","spawn","speak",
  "special","speed","spell","spend","sphere","spice","spider","spike","spin","spirit",
  "split","spoil","sponsor","spoon","sport","spot","spray","spread","spring","spy",
  "square","squeeze","squirrel","stable","stadium","staff","stage","stairs","stamp","stand",
  "start","state","stay","steak","steel","stem","step","stereo","stick","still",
  "sting","stock","stomach","stone","stool","story","stove","strategy","street","strike",
  "strong","struggle","student","stuff","stumble","style","subject","submit","subway","success",
  "such","sudden","suffer","sugar","suggest","suit","sun","sunny","sunset","super",
  "supply","supreme","sure","surface","surge","surprise","surround","survey","suspect","sustain",
  "swallow","swamp","swap","swarm","swear","sweet","swift","swim","swing","switch",
  "sword","symbol","symptom","syrup","system","table","tackle","tag","tail","talent",
  "talk","tank","tape","target","task","taste","tattoo","taxi","teach","team",
  "tell","ten","tenant","tennis","tent","term","test","text","thank","that",
  "theme","then","theory","there","they","thing","this","thought","three","thrive",
  "throw","thumb","thunder","ticket","tide","tiger","tilt","timber","time","tiny",
  "tip","tired","tissue","title","toast","tobacco","today","toddler","toe","together",
  "toilet","token","tomato","tomorrow","tone","tongue","tonight","tool","tooth","top",
  "topic","topple","torch","tornado","tortoise","toss","total","tourist","toward","tower",
  "town","toy","track","trade","traffic","tragic","train","transfer","trap","trash",
  "travel","tray","treat","tree","trend","trial","tribe","trick","trigger","trim",
  "trip","trophy","trouble","truck","true","truly","trumpet","trust","truth","try",
  "tube","tuition","tumble","tuna","tunnel","turkey","turn","turtle","twelve","twenty",
  "twice","twin","twist","two","type","typical","ugly","umbrella","unable","unaware",
  "uncle","uncover","under","undo","unfair","unfold","unhappy","uniform","unique","unit",
  "universe","unknown","unlock","until","unusual","unveil","update","upgrade","uphold","upon",
  "upper","upset","urban","urge","usage","use","used","useful","useless","usual",
  "utility","vacant","vacuum","vague","valid","valley","valve","van","vanish","vapor",
  "various","vast","vault","vehicle","velvet","vendor","venture","venue","verb","verify",
  "version","very","vessel","veteran","viable","vibrant","vicious","victory","video","view",
  "village","vintage","violent","virtual","virus","visa","visit","visual","vital","vivid",
  "vocal","voice","void","volcano","volume","vote","voyage","wage","wagon","wait",
  "walk","wall","walnut","want","warfare","warm","warrior","wash","wasp","waste",
  "water","wave","way","wealth","weapon","wear","weasel","weather","web","wedding",
  "weekend","weird","welcome","west","wet","whale","what","wheat","wheel","when",
  "where","whip","whisper","wide","width","wife","wild","will","win","window",
  "wine","wing","wink","winner","winter","wire","wisdom","wise","wish","witness",
  "wolf","woman","wonder","wood","wool","word","work","world","worry","worth",
  "wrap","wreck","wrestle","wrist","write","wrong","yard","year","yellow","you",
  "young","youth","zebra","zero","zone","zoo"
]

function generateMnemonic(): string {
  const indices = new Uint16Array(12)
  crypto.getRandomValues(indices)
  const words: string[] = []
  for (let i = 0; i < 12; i++) {
    words.push(WORDS[indices[i] % WORDS.length])
  }
  return words.join(" ")
}

// ────────────────────────────────────────────────────────────────────────────
// Crypto helpers (Deno native)
// ────────────────────────────────────────────────────────────────────────────

async function hmacSha256Hex(secretHex: string, data: string): Promise<string> {
  const keyBytes = hexToBytes(secretHex)
  const key = await crypto.subtle.importKey(
    "raw", keyBytes,
    { name: "HMAC", hash: "SHA-256" },
    false, ["sign"]
  )
  const sig = await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(data))
  return bytesToHex(new Uint8Array(sig))
}

function sha256Hex(data: string): string {
  const hash = new Uint8Array(
    crypto.subtle.digestSync("SHA-256", new TextEncoder().encode(data))
  )
  return bytesToHex(hash)
}

function bytesToHex(bytes: Uint8Array): string {
  return Array.from(bytes).map(b => b.toString(16).padStart(2, "0")).join("")
}

function hexToBytes(hex: string): Uint8Array {
  const bytes = new Uint8Array(hex.length / 2)
  for (let i = 0; i < hex.length; i += 2) {
    bytes[i / 2] = parseInt(hex.substring(i, i + 2), 16)
  }
  return bytes
}

function base64UrlEncode(data: Uint8Array): string {
  return btoa(String.fromCharCode(...data))
    .replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "")
}

async function signJwt(payload: Record<string, unknown>, secret: string): Promise<string> {
  const header = { alg: "HS256", typ: "JWT" }
  const headerB64 = base64UrlEncode(new TextEncoder().encode(JSON.stringify(header)))
  const payloadB64 = base64UrlEncode(new TextEncoder().encode(JSON.stringify(payload)))
  const message = `${headerB64}.${payloadB64}`

  const key = await crypto.subtle.importKey(
    "raw", new TextEncoder().encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false, ["sign"]
  )
  const sig = await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(message))
  const sigB64 = base64UrlEncode(new Uint8Array(sig))

  return `${message}.${sigB64}`
}

async function verifyRsaSignature(
  publicKeyPem: string,
  data: string,
  signatureBase64: string,
): Promise<boolean> {
  const pemHeader = "-----BEGIN PUBLIC KEY-----"
  const pemFooter = "-----END PUBLIC KEY-----"
  let b64 = publicKeyPem
  if (b64.includes(pemHeader)) {
    b64 = b64.replace(pemHeader, "").replace(pemFooter, "").replace(/\s/g, "")
  }
  const derBytes = Uint8Array.from(atob(b64), c => c.charCodeAt(0))

  const key = await crypto.subtle.importKey(
    "spki", derBytes,
    { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
    false, ["verify"]
  )

  const sigBytes = Uint8Array.from(atob(signatureBase64), c => c.charCodeAt(0))

  return await crypto.subtle.verify(
    { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
    key, sigBytes, new TextEncoder().encode(data)
  )
}

// ────────────────────────────────────────────────────────────────────────────
// Main handler
// ────────────────────────────────────────────────────────────────────────────

serve(async (req) => {
  if (req.method === "OPTIONS") {
    return new Response("ok", {
      headers: {
        "Access-Control-Allow-Origin": "*",
        "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
      },
    })
  }

  try {
    const APP_SECRET = Deno.env.get("OVERLAY_SYNC_SECRET") ?? ""
    const JWT_SECRET = Deno.env.get("SUPABASE_JWT_SECRET") ?? ""
    if (!APP_SECRET || !JWT_SECRET) {
      return new Response(JSON.stringify({ error: "Server misconfiguration: missing secrets" }), { status: 500 })
    }

    const supabase = createClient(
      Deno.env.get("SUPABASE_URL") ?? "",
      Deno.env.get("SUPABASE_SERVICE_ROLE_KEY") ?? "",
    )

    const body = await req.json().catch(() => ({}))
    const {
      steam_id,
      client_public_key,
      hmac_signature,
      timestamp,
      signature,
      nonce,
      new_public_key,
      recovery_signature,
      mnemonic_token,
    } = body

    if (!steam_id || typeof steam_id !== "string") {
      return new Response(JSON.stringify({ error: "steam_id is required" }), { status: 400 })
    }

    // ── Flow 1: First-time / new key registration ──
    if (hmac_signature) {
      if (!client_public_key || !timestamp) {
        return new Response(JSON.stringify({ error: "client_public_key and timestamp required for registration" }), { status: 400 })
      }

      // Reject timestamps older than 5 minutes
      const ts = parseInt(timestamp, 10)
      if (isNaN(ts) || Math.abs(Date.now() - ts) > 300_000) {
        return new Response(JSON.stringify({ error: "Timestamp too far from server time" }), { status: 400 })
      }

      const expectedHmac = await hmacSha256Hex(APP_SECRET, steam_id + timestamp + client_public_key)
      if (hmac_signature !== expectedHmac) {
        return new Response(JSON.stringify({ error: "Untrusted client HMAC signature" }), { status: 403 })
      }

      // Check if steam_id already has a registered key
      const { data: existing } = await supabase
        .from("client_keys")
        .select("steam_id")
        .eq("steam_id", steam_id)
        .maybeSingle()

      if (existing) {
        return new Response(
          JSON.stringify({ error: "Steam ID already registered. Use recovery code to migrate devices." }),
          { status: 409 },
        )
      }

      // Ensure user profile row exists
      await supabase
        .from("user_profiles")
        .upsert({ steam_id, subscription_tier: "free", sync_accepted: true }, { onConflict: "steam_id" })

      // Generate mnemonic and hash
      const mnemonic = generateMnemonic()
      const recoveryHash = sha256Hex(mnemonic)

      // Save client key
      await supabase
        .from("client_keys")
        .insert({ steam_id, public_key: client_public_key, recovery_hash: recoveryHash })

      // Generate custom JWT
      const token = await signJwt(
        {
          sub: steam_id,
          role: "authenticated",
          aud: "authenticated",
          exp: Math.floor(Date.now() / 1000) + 86_400, // 24 hours
          iat: Math.floor(Date.now() / 1000),
          steam_id,
        },
        JWT_SECRET,
      )

      return new Response(JSON.stringify({ token, recovery_code: mnemonic }), {
        headers: { "Content-Type": "application/json" },
      })
    }

    // ── Flow 2: Quick session refresh ──
    if (signature && timestamp && nonce) {
      const { data: keyRow, error: fetchErr } = await supabase
        .from("client_keys")
        .select("public_key")
        .eq("steam_id", steam_id)
        .single()

      if (fetchErr || !keyRow) {
        return new Response(JSON.stringify({ error: "No key registered for this steam_id" }), { status: 401 })
      }

      const isValid = await verifyRsaSignature(keyRow.public_key, timestamp + nonce, signature)
      if (!isValid) {
        return new Response(JSON.stringify({ error: "Signature verification failed" }), { status: 401 })
      }

      const token = await signJwt(
        {
          sub: steam_id,
          role: "authenticated",
          aud: "authenticated",
          exp: Math.floor(Date.now() / 1000) + 86_400,
          iat: Math.floor(Date.now() / 1000),
          steam_id,
        },
        JWT_SECRET,
      )

      return new Response(JSON.stringify({ token }), {
        headers: { "Content-Type": "application/json" },
      })
    }

    // ── Flow 3: Device recovery / migration ──
    if (recovery_signature && mnemonic_token && new_public_key) {
      const { data: keyRow, error: fetchErr } = await supabase
        .from("client_keys")
        .select("recovery_hash")
        .eq("steam_id", steam_id)
        .single()

      if (fetchErr || !keyRow) {
        return new Response(JSON.stringify({ error: "Steam ID not registered" }), { status: 404 })
      }

      const expectedHash = sha256Hex(mnemonic_token)
      if (expectedHash !== keyRow.recovery_hash) {
        return new Response(JSON.stringify({ error: "Invalid recovery mnemonic" }), { status: 401 })
      }

      // Verify the recovery_signature: signed by old private key over steam_id + new_public_key
      // The C# client will sign with the OLD private key (from backup) to authorize the key change
      // We need the old public key. Let's fetch it.
      const { data: oldKeyRow } = await supabase
        .from("client_keys")
        .select("public_key")
        .eq("steam_id", steam_id)
        .single()

      if (oldKeyRow) {
        const recoveryData = steam_id + new_public_key
        const recoveryValid = await verifyRsaSignature(oldKeyRow.public_key, recoveryData, recovery_signature)
        if (!recoveryValid) {
          return new Response(JSON.stringify({ error: "Recovery signature verification failed" }), { status: 401 })
        }
      }

      // Update public key + generate new recovery hash
      const newMnemonic = generateMnemonic()
      const newRecoveryHash = sha256Hex(newMnemonic)

      await supabase
        .from("client_keys")
        .update({ public_key: new_public_key, recovery_hash: newRecoveryHash })
        .eq("steam_id", steam_id)

      const token = await signJwt(
        {
          sub: steam_id,
          role: "authenticated",
          aud: "authenticated",
          exp: Math.floor(Date.now() / 1000) + 86_400,
          iat: Math.floor(Date.now() / 1000),
          steam_id,
        },
        JWT_SECRET,
      )

      return new Response(JSON.stringify({ token, new_recovery_code: newMnemonic }), {
        headers: { "Content-Type": "application/json" },
      })
    }

    return new Response(JSON.stringify({ error: "Invalid handshake parameters" }), { status: 400 })
  } catch (error) {
    return new Response(JSON.stringify({ error: error.message }), { status: 500 })
  }
})
