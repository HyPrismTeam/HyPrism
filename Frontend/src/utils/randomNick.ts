export function generateRandomNick(): string {
  // Short adjectives (max 5 chars) + short nouns (max 6 chars) + 4-digit number = max 15 chars
  // Keep the same structure used in ProfileEditor (e.g. "ZestyShark3466").
  const adjectives = [
    'Happy', 'Swift', 'Brave', 'Noble', 'Quiet', 'Bold', 'Lucky', 'Epic',
    'Jolly', 'Lunar', 'Solar', 'Azure', 'Royal', 'Foxy', 'Wacky', 'Zesty',
    'Fizzy', 'Dizzy', 'Funky', 'Jazzy', 'Snowy', 'Rainy', 'Sunny', 'Windy',
    'Fiery', 'Icy', 'Misty', 'Dusty', 'Rusty', 'Shiny', 'Silky', 'Fuzzy',
  ];
  const nouns = [
    'Panda', 'Tiger', 'Wolf', 'Dragon', 'Knight', 'Ranger', 'Mage', 'Fox',
    'Bear', 'Eagle', 'Hawk', 'Lion', 'Falcon', 'Raven', 'Owl', 'Shark',
    'Cobra', 'Viper', 'Lynx', 'Badger', 'Otter', 'Mantis', 'Pirate', 'Ninja',
    'Viking', 'Wizard', 'Scout', 'Hero', 'Ace', 'Star', 'King', 'Queen',
  ];

  const adj = adjectives[Math.floor(Math.random() * adjectives.length)];
  const noun = nouns[Math.floor(Math.random() * nouns.length)];
  const num = Math.floor(Math.random() * 9000) + 1000; // 4-digit number
  const name = `${adj}${noun}${num}`;
  return name.length <= 16 ? name : name.substring(0, 16);
}
