import React, { Component } from 'react';
import { View, Text, Button } from 'react-native';

export default class Publicacion extends Component {
  render() {
    const { navigation } = this.props;
    return (
      <View style={styles.container}>
        <Text> Publicacion </Text>
        <Button title="Comentarios" onPress={() => { navigation.navigate('Comentarios'); }} />
        <Button title="Autor" onPress={() => { navigation.navigate('Autor'); }} />
      </View>
    );
  }
}

const styles = ({
  container: {
    flex: 1,
    justifyContent: 'center',
    alignItems: 'center',
    backgroundColor: '#2c3e50',
  },
});
