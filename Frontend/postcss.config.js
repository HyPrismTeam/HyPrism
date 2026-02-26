// PostCSS config for Sciter target.
// - No Tailwind (generates @layer which Sciter rejects)
// - No autoprefixer (adds -webkit-/-moz- prefixes Sciter doesn't need and
//   logs "unrecognized property name syntax" warnings for)

export default {
  plugins: [],
};
