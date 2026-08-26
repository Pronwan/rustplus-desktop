import React, { useEffect, useState, useMemo } from 'react';
import {
  Box,
  Typography,
  Tabs,
  Tab,
  Button,
  ButtonGroup,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  IconButton,
  Tooltip,
  Collapse,
  TextField,
  InputAdornment,
  useMediaQuery,
  useTheme
} from '@mui/material';
import ViewListIcon from '@mui/icons-material/ViewList';
import GridViewIcon from '@mui/icons-material/GridView';
import SearchIcon from '@mui/icons-material/Search';
import FunctionsIcon from '@mui/icons-material/Functions';
import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
import { RUST_RECIPES, getItemImageSlug, Recipe } from '../../domain/recipes/recipesData.ts';
import { RecipeEngine } from '../../domain/recipes/recipeEngine.ts';

const recipeEngine = new RecipeEngine(RUST_RECIPES);

const CATEGORIES = ['All', 'tea', 'pie', 'food', 'resource'] as const;

/**
 * Item icon framed in a Rust-style inventory slot. The dark slot keeps
 * inherently dark item art (teas) legible in light mode instead of showing
 * as a muddy blob on the light panel.
 */
const ItemIcon: React.FC<{ name: string; size: number }> = ({ name, size }) => (
  <Box
    sx={{
      width: size + 8,
      height: size + 8,
      flexShrink: 0,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      backgroundColor: 'var(--gl-item-slot-bg)',
      border: '1px solid var(--gl-item-slot-border)',
      borderRadius: '4px'
    }}
  >
    <Box
      component="img"
      src={`./img/items/${getItemImageSlug(name)}.webp`}
      alt={name}
      sx={{ width: size, height: size, objectFit: 'contain' }}
    />
  </Box>
);

export const RecipesPage: React.FC = () => {
  const theme = useTheme();
  const isCompact = useMediaQuery(theme.breakpoints.down('sm'));
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState<string>('All');
  const [multiplier, setMultiplier] = useState<number>(1);
  const [viewMode, setViewMode] = useState<'list' | 'grid'>('list');
  const [expandedRowIds, setExpandedRowIds] = useState<Record<string, boolean>>({});

  useEffect(() => {
    try {
      const raw = sessionStorage.getItem('GL_RECIPE_HANDOFF_V1');
      if (!raw) return;
      sessionStorage.removeItem('GL_RECIPE_HANDOFF_V1');
      const handoff = JSON.parse(raw) as { name?: string; multiplier?: number };
      const recipe = RUST_RECIPES.find((item) => item.name === handoff.name);
      if (!recipe) return;
      setSearch(recipe.name);
      setCategory(recipe.category);
      setMultiplier(Math.max(1, Math.ceil(Number(handoff.multiplier) || 1)));
    } catch {
      sessionStorage.removeItem('GL_RECIPE_HANDOFF_V1');
    }
  }, []);

  useEffect(() => {
    if (isCompact) setViewMode('grid');
  }, [isCompact]);

  const toggleRowExpansion = (id: string) => {
    setExpandedRowIds((prev) => ({ ...prev, [id]: !prev[id] }));
  };

  const filteredRecipes = useMemo(() => {
    return RUST_RECIPES.filter((r) => {
      if (category !== 'All' && r.category !== category) return false;
      if (search.trim() && !r.name.toLowerCase().includes(search.toLowerCase())) return false;
      return true;
    });
  }, [search, category]);

  return (
    <Box sx={{ maxWidth: 1200, mx: 'auto', p: { xs: 2, md: 3 } }}>
      <Typography component="h1" variant="h5" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)', mb: 0.5 }}>
        Tea Recipes
      </Typography>
      <Typography variant="body2" sx={{ color: 'var(--gl-text-muted)', mb: 2 }}>
        Browse Rust crafting recipes and scale ingredient totals for your next run.
      </Typography>
      {/* Control & Filter Bar */}
      <Box
        sx={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          flexWrap: 'wrap',
          gap: 2,
          mb: 2.5,
          borderBottom: '1px solid var(--gl-border)',
          pb: 1.5,
          position: { xs: 'sticky', sm: 'static' },
          top: 0,
          zIndex: 5,
          backgroundColor: 'var(--gl-app-bg)'
        }}
      >
        {/* Category Tabs */}
        <Tabs
          variant="scrollable"
          scrollButtons="auto"
          value={category}
          onChange={(_, val) => setCategory(val)}
          textColor="inherit"
          sx={{
            minHeight: 36,
            '& .MuiTabs-indicator': { backgroundColor: 'var(--gl-primary)', height: 2 }
          }}
        >
          {CATEGORIES.map((cat) => (
            <Tab
              key={cat}
              value={cat}
              label={cat.toUpperCase()}
              sx={{
                minHeight: 36,
                py: 0.5,
                px: 2,
                fontSize: '0.8rem',
                fontWeight: 700,
                opacity: 1,
                color: category === cat ? 'var(--gl-primary)' : 'var(--gl-text-muted)'
              }}
            />
          ))}
        </Tabs>

        {/* Right Controls: Search, Multiplier, List/Grid View */}
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, flexWrap: 'wrap' }}>
          {/* Search Box */}
          <TextField
            size="small"
            placeholder="Search recipes..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            slotProps={{
              htmlInput: { 'aria-label': 'Search tea recipes' },
              input: {
                startAdornment: (
                  <InputAdornment position="start">
                    <SearchIcon sx={{ fontSize: 16, color: 'var(--gl-text-muted)' }} />
                  </InputAdornment>
                )
              }
            }}
            sx={{
              width: 180,
              '& .MuiInputBase-root': {
                backgroundColor: 'var(--gl-panel-bg)',
                color: 'var(--gl-text-primary)',
                fontSize: '0.8rem',
                fontFamily: 'monospace',
                height: 32
              }
            }}
          />

          {/* Multiplier */}
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
            <ButtonGroup size="small" variant="outlined" aria-label="Recipe quantity multiplier">
              {[1, 5, 10, 50].map((m) => (
                <Button
                  key={m}
                  onClick={() => setMultiplier(m)}
                  sx={{
                    fontSize: '0.75rem',
                    py: 0.25,
                    px: 1.2,
                    backgroundColor: multiplier === m ? 'var(--gl-primary)' : 'transparent',
                    color: multiplier === m ? 'var(--gl-on-accent)' : 'var(--gl-text-secondary)',
                    borderColor: 'var(--gl-border-strong)',
                    fontWeight: 700,
                    '&:hover': {
                      backgroundColor: multiplier === m ? 'var(--gl-primary)' : 'rgba(255,255,255,0.05)'
                    }
                  }}
                >
                  {m}x
                </Button>
              ))}
            </ButtonGroup>
          </Box>

          {/* View Mode Toggle */}
          <ButtonGroup size="small" variant="outlined">
            <Tooltip title="List / Table View">
              <Button
                aria-label="Use recipe table view"
                aria-pressed={viewMode === 'list'}
                onClick={() => setViewMode('list')}
                sx={{
                  py: 0.25,
                  px: 1,
                  backgroundColor: viewMode === 'list' ? 'rgba(0, 229, 255, 0.15)' : 'transparent',
                  color: viewMode === 'list' ? 'var(--gl-primary)' : 'var(--gl-text-muted)',
                  borderColor: 'var(--gl-border-strong)'
                }}
              >
                <ViewListIcon sx={{ fontSize: 18 }} />
              </Button>
            </Tooltip>
            <Tooltip title="Grid View">
              <Button
                aria-label="Use recipe card view"
                aria-pressed={viewMode === 'grid'}
                onClick={() => setViewMode('grid')}
                sx={{
                  py: 0.25,
                  px: 1,
                  backgroundColor: viewMode === 'grid' ? 'rgba(0, 229, 255, 0.15)' : 'transparent',
                  color: viewMode === 'grid' ? 'var(--gl-primary)' : 'var(--gl-text-muted)',
                  borderColor: 'var(--gl-border-strong)'
                }}
              >
                <GridViewIcon sx={{ fontSize: 18 }} />
              </Button>
            </Tooltip>
          </ButtonGroup>
        </Box>
      </Box>

      <Typography variant="caption" sx={{ display: 'block', color: 'var(--gl-text-muted)', mb: 1.5 }}>
        Showing {filteredRecipes.length} recipe{filteredRecipes.length === 1 ? '' : 's'}
      </Typography>

      {/* VIEW 1: AUTHENTIC LIST / TABLE VIEW */}
      {viewMode === 'list' && (
        <TableContainer
          component={Paper}
          variant="outlined"
          sx={{
            backgroundColor: 'var(--gl-panel-bg)',
            borderColor: 'var(--gl-surface)',
            borderRadius: '4px'
          }}
        >
          <Table size="small">
            <TableHead sx={{ backgroundColor: 'var(--gl-panel-header-bg)' }}>
              <TableRow>
                <TableCell sx={{ color: 'var(--gl-text-muted)', fontWeight: 700, fontFamily: 'monospace', fontSize: '0.8rem', py: 1, width: '28%' }}>
                  Item
                </TableCell>
                <TableCell sx={{ color: 'var(--gl-text-muted)', fontWeight: 700, fontFamily: 'monospace', fontSize: '0.8rem', py: 1 }}>
                  Recipe
                </TableCell>
                <TableCell align="right" sx={{ color: 'var(--gl-text-muted)', fontWeight: 700, fontFamily: 'monospace', fontSize: '0.8rem', py: 1, width: '8%' }}>
                  Σ
                </TableCell>
              </TableRow>
            </TableHead>

            <TableBody>
              {filteredRecipes.map((recipe) => {
                const isExpanded = !!expandedRowIds[recipe.id];
                const rawMaterials = recipeEngine.expandItem(recipe.name, multiplier);
                const hasRawSubBreakdown =
                  rawMaterials.length > 0 &&
                  (rawMaterials.length !== recipe.ingredients.length ||
                    rawMaterials.some((r, i) => r.item !== recipe.ingredients[i]?.item));

                return (
                  <React.Fragment key={recipe.id}>
                    <TableRow
                      hover
                      sx={{
                        backgroundColor: isExpanded ? 'rgba(255, 255, 255, 0.02)' : 'transparent',
                        borderColor: 'var(--gl-surface)',
                        '&:hover': { backgroundColor: 'rgba(255, 255, 255, 0.03)' }
                      }}
                    >
                      {/* Column 1: Item Name and Icon */}
                      <TableCell sx={{ borderColor: 'var(--gl-elevated-bg)', py: 1.2 }}>
                        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
                          <ItemIcon name={recipe.name} size={28} />
                          <Typography
                            variant="body2"
                            sx={{
                              fontWeight: 700,
                              color: 'var(--gl-text-primary)',
                              fontFamily: '"Roboto Mono", monospace',
                              fontSize: '0.85rem'
                            }}
                          >
                            {recipe.name}
                          </Typography>
                        </Box>
                      </TableCell>

                      {/* Column 2: Recipe (Ingredients -> Arrow -> Output) */}
                      <TableCell sx={{ borderColor: 'var(--gl-elevated-bg)', py: 1.2 }}>
                        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
                          {/* Ingredients List */}
                          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
                            {recipe.ingredients.map((ing, idx) => (
                              <Box key={idx} sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                                <ItemIcon name={ing.item} size={24} />
                                <Typography
                                  variant="caption"
                                  sx={{
                                    color: 'var(--gl-text-primary)',
                                    fontWeight: 700,
                                    fontFamily: 'monospace',
                                    fontSize: '0.8rem'
                                  }}
                                >
                                  {ing.quantity * multiplier}
                                </Typography>
                              </Box>
                            ))}
                          </Box>

                          {/* Arrow */}
                          <ArrowForwardIcon sx={{ fontSize: 16, color: 'var(--gl-text-muted)' }} />

                          {/* Output Product */}
                          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                            <ItemIcon name={recipe.output.item} size={26} />
                            <Typography
                              variant="caption"
                              sx={{
                                color: 'var(--gl-primary)',
                                fontWeight: 800,
                                fontFamily: 'monospace',
                                fontSize: '0.85rem'
                              }}
                            >
                              {recipe.output.quantity * multiplier}
                            </Typography>
                          </Box>
                        </Box>
                      </TableCell>

                      {/* Column 3: Sigma (Raw Materials Breakdown) */}
                      <TableCell align="right" sx={{ borderColor: 'var(--gl-elevated-bg)', py: 1.2 }}>
                        {hasRawSubBreakdown ? (
                          <Tooltip title={isExpanded ? 'Hide Raw Materials Breakdown' : 'Show Total Raw Base Materials (Σ)'}>
                            <IconButton
                              aria-label={`${isExpanded ? 'Hide' : 'Show'} raw materials for ${recipe.name}`}
                              aria-expanded={isExpanded}
                              aria-controls={`raw-materials-${recipe.id}`}
                              size="small"
                              onClick={() => toggleRowExpansion(recipe.id)}
                              sx={{
                                color: isExpanded ? 'var(--gl-primary)' : 'var(--gl-text-muted)',
                                backgroundColor: isExpanded ? 'rgba(0, 229, 255, 0.1)' : 'transparent',
                                '&:hover': { color: 'var(--gl-primary)' }
                              }}
                            >
                              <FunctionsIcon sx={{ fontSize: 18 }} />
                            </IconButton>
                          </Tooltip>
                        ) : (
                          <Typography variant="caption" sx={{ color: 'var(--gl-text-faint)' }}>-</Typography>
                        )}
                      </TableCell>
                    </TableRow>

                    {/* Expandable Sigma Sub-Row */}
                    {hasRawSubBreakdown && (
                      <TableRow id={`raw-materials-${recipe.id}`} sx={{ backgroundColor: 'rgba(0, 0, 0, 0.25)' }}>
                        <TableCell colSpan={3} sx={{ py: 0, px: 3, borderColor: 'var(--gl-elevated-bg)' }}>
                          <Collapse in={isExpanded} timeout="auto" unmountOnExit>
                            <Box sx={{ py: 1.5, display: 'flex', alignItems: 'center', gap: 2, flexWrap: 'wrap' }}>
                              <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontWeight: 700, fontFamily: 'monospace' }}>
                                Total Raw Materials:
                              </Typography>
                              <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, flexWrap: 'wrap' }}>
                                {rawMaterials.map((raw, rIdx) => (
                                  <Box key={rIdx} sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
                                    <ItemIcon name={raw.item} size={20} />
                                    <Typography variant="caption" sx={{ color: 'var(--gl-text-secondary)', fontFamily: 'monospace' }}>
                                      {raw.item}:
                                    </Typography>
                                    <Typography variant="caption" sx={{ color: 'var(--gl-text-primary)', fontWeight: 800, fontFamily: 'monospace' }}>
                                      {raw.quantity}
                                    </Typography>
                                  </Box>
                                ))}
                              </Box>
                            </Box>
                          </Collapse>
                        </TableCell>
                      </TableRow>
                    )}
                  </React.Fragment>
                );
              })}
            </TableBody>
          </Table>
        </TableContainer>
      )}

      {/* VIEW 2: GRID / CARD VIEW */}
      {viewMode === 'grid' && (
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', lg: 'repeat(3, 1fr)' },
            gap: 2
          }}
        >
          {filteredRecipes.map((recipe) => {
            const rawMaterials = recipeEngine.expandItem(recipe.name, multiplier);

            return (
              <Paper
                key={recipe.id}
                variant="outlined"
                sx={{
                  backgroundColor: 'var(--gl-panel-header-bg)',
                  borderColor: 'var(--gl-border)',
                  borderRadius: '6px',
                  p: 2,
                  display: 'flex',
                  flexDirection: 'column'
                }}
              >
                {/* Header */}
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, mb: 1.5 }}>
                  <ItemIcon name={recipe.name} size={32} />
                  <Box>
                    <Typography
                      variant="subtitle2"
                      sx={{
                        fontWeight: 700,
                        color: 'var(--gl-text-primary)',
                        fontFamily: '"Roboto Mono", monospace',
                        lineHeight: 1.2
                      }}
                    >
                      {recipe.name}
                    </Typography>
                    <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontWeight: 600 }}>
                      Yield: {recipe.output.quantity * multiplier}x
                    </Typography>
                  </Box>
                </Box>

                {/* Direct Ingredients */}
                <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.5, mb: 1.5 }}>
                  {recipe.ingredients.map((ing, idx) => (
                    <Box key={idx} sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                      <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
                        <ItemIcon name={ing.item} size={18} />
                        <Typography variant="caption" sx={{ color: 'var(--gl-text-secondary)', fontFamily: 'monospace' }}>
                          {ing.item}
                        </Typography>
                      </Box>
                      <Typography variant="caption" sx={{ color: 'var(--gl-text-primary)', fontWeight: 700, fontFamily: 'monospace' }}>
                        {ing.quantity * multiplier}
                      </Typography>
                    </Box>
                  ))}
                </Box>

                {/* Raw Breakdown */}
                <Box
                  sx={{
                    mt: 'auto',
                    p: 1.2,
                    backgroundColor: 'var(--gl-panel-bg)',
                    border: '1px solid var(--gl-surface)',
                    borderRadius: '4px'
                  }}
                >
                  <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontWeight: 700, display: 'block', mb: 0.5, fontSize: '0.7rem' }}>
                    Total Raw Materials:
                  </Typography>
                  <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.3 }}>
                    {rawMaterials.map((raw, rIdx) => (
                      <Box key={rIdx} sx={{ display: 'flex', justifyContent: 'space-between' }}>
                        <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontFamily: 'monospace', fontSize: '0.75rem' }}>
                          {raw.item}
                        </Typography>
                        <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontWeight: 800, fontFamily: 'monospace', fontSize: '0.75rem' }}>
                          {raw.quantity}
                        </Typography>
                      </Box>
                    ))}
                  </Box>
                </Box>
              </Paper>
            );
          })}
        </Box>
      )}
    </Box>
  );
};
