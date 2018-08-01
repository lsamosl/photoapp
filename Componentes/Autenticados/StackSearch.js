import { StackNavigator } from 'react-navigation';
import Autor from './Profile';
import Publicacion from './Publicacion';
import Comentarios from './Comentarios';
import Search from './Search';

const StackSearch = StackNavigator(
  {
    Search: {
      screen: Search,
    },
    Publicacion: {
      screen: Publicacion,
    },
    Autor: {
      screen: Autor,
    },
    Comentarios: {
      screen: Comentarios,
    },
  },
);

export { StackSearch };
